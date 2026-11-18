// ratcapture-linux: PC audio helper for the tablet on Linux. Moves a program's streams to the null sink "rat_capture"
// (silent on the PC) and records its monitor. Song and keys come from MPRIS. Works on PulseAudio and PipeWire.
//
// Wine passes no pipes to Linux programs, so with a port the mod talks over TCP on 127.0.0.1. A frame is a type byte
// ('A' sound, 'S' line), a 4-byte little-endian length and the payload. Without a port it uses stderr/stdout/stdin.
//
//   ratcapture-linux run <program> <rate> [port]
//   ratcapture-linux list [port]
//   ratcapture-linux cleanup [port]
//   ratcapture-linux diag [port]
//   ratcapture-linux tone <seconds>
//   ratcapture-linux test-sink <name>

#define _GNU_SOURCE
#include <arpa/inet.h>
#include <dlfcn.h>
#include <errno.h>
#include <gnu/libc-version.h>
#include <limits.h>
#include <math.h>
#include <netinet/in.h>
#include <poll.h>
#include <pthread.h>
#include <signal.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>
#include <sys/socket.h>
#include <time.h>
#include <unistd.h>
#include "mpris.h"
#include "pulse_api.h"

#define SINK_NAME "rat_capture"
#define CHANNELS 2
#define MAX_STREAMS 64
#define HOUSEKEEPING_MS 500
// A program reports "paused" before its last sound is out, so streams go back only this much later.
#define PAUSED_SETTLE_MS 500
#define PAUSE_TIMEOUT_MS 3000
#define LEVEL_REPORT_MS 30000

#define RAT_PULSE_DEFINE(ret, name, args) ret (*p_##name) args;
RAT_PULSE_FUNCTIONS(RAT_PULSE_DEFINE)
#undef RAT_PULSE_DEFINE

int pulse_load(const char **error)
{
    void *library = dlopen("libpulse.so.0", RTLD_NOW | RTLD_LOCAL);
    if (!library)
    {
        *error = dlerror();
        return -1;
    }
#define RAT_PULSE_LOAD(ret, name, args) \
    if (!(p_##name = (ret (*) args)dlsym(library, #name))) { *error = "libpulse.so.0 lacks " #name; return -1; }
    RAT_PULSE_FUNCTIONS(RAT_PULSE_LOAD)
#undef RAT_PULSE_LOAD
    return 0;
}

static pa_threaded_mainloop *loop;
static pa_context *context;

static void signal_loop(void) { p_pa_threaded_mainloop_signal(loop, 0); }
static void on_state(pa_context *c, void *userdata) { (void)c; (void)userdata; signal_loop(); }
static void on_stream_state(pa_stream *s, void *userdata) { (void)s; (void)userdata; signal_loop(); }

static void on_success(pa_context *c, int success, void *userdata)
{
    (void)c;
    if (userdata) *(int *)userdata = success;
    signal_loop();
}

// Call with the loop locked.
static void finish(pa_operation *operation)
{
    if (!operation) return;
    while (p_pa_operation_get_state(operation) == PA_OPERATION_RUNNING)
        p_pa_threaded_mainloop_wait(loop);
    p_pa_operation_unref(operation);
}

static long now_ms(void)
{
    struct timespec t;
    clock_gettime(CLOCK_MONOTONIC, &t);
    return t.tv_sec * 1000L + t.tv_nsec / 1000000L;
}

static int sock = -1;
static int terminal_lines_to_stderr;
static pthread_mutex_t out_lock = PTHREAD_MUTEX_INITIALIZER;
static volatile int output_gone;

static int write_all(int fd, const void *data, size_t length)
{
    const char *p = data;
    while (length > 0)
    {
        ssize_t done = fd == sock ? send(fd, p, length, MSG_NOSIGNAL) : write(fd, p, length);
        if (done < 0 && errno == EINTR) continue;
        if (done <= 0) return -1;
        p += done;
        length -= (size_t)done;
    }
    return 0;
}

static void emit(char type, const void *data, size_t length)
{
    if (output_gone) return;
    pthread_mutex_lock(&out_lock);
    int failed;
    if (sock >= 0)
    {
        unsigned char header[5] = { (unsigned char)type, length & 0xff, (length >> 8) & 0xff, (length >> 16) & 0xff,
                                    (length >> 24) & 0xff };
        failed = write_all(sock, header, 5) != 0 || write_all(sock, data, length) != 0;
    }
    else if (type == 'A')
        failed = write_all(1, data, length) != 0;
    else
    {
        FILE *to = terminal_lines_to_stderr ? stderr : stdout;
        failed = fwrite(data, 1, length, to) != length || fputc('\n', to) == EOF || fflush(to) != 0;
    }
    if (failed) output_gone = 1;
    pthread_mutex_unlock(&out_lock);
}

// Tabs and line breaks in a field become spaces, as in CaptureProtocol.Clean.
static void clean(char *field)
{
    for (char *c = field; *c; c++)
        if (*c == '\t' || *c == '\n' || *c == '\r') *c = ' ';
}

// The last argument is NULL.
static void line(const char *first, ...)
{
    char text[2048];
    size_t used = 0;
    va_list fields;
    va_start(fields, first);
    for (const char *field = first; field; field = va_arg(fields, const char *))
    {
        char part[1024];
        snprintf(part, sizeof part, "%s", field);
        clean(part);
        used += (size_t)snprintf(text + used, sizeof text - used, "%s%s", used ? "\t" : "", part);
        if (used >= sizeof text) { used = sizeof text - 1; break; }
    }
    va_end(fields);
    emit('S', text, used);
}

static void log_line(const char *format, ...)
{
    char text[1024];
    va_list args;
    va_start(args, format);
    vsnprintf(text, sizeof text, format, args);
    va_end(args);
    line("log", text, NULL);
}

static int connect_mod(const char *port)
{
    sock = socket(AF_INET, SOCK_STREAM, 0);
    struct sockaddr_in address = { 0 };
    address.sin_family = AF_INET;
    address.sin_port = htons((uint16_t)atoi(port));
    address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    if (sock < 0 || connect(sock, (struct sockaddr *)&address, sizeof address) != 0)
    {
        fprintf(stderr, "ratcapture-linux: cannot connect to the game on port %s: %s\n", port, strerror(errno));
        return -1;
    }
    return 0;
}

static int connect_server(void)
{
    const char *error;
    if (pulse_load(&error) != 0)
    {
        line("error", "No PulseAudio client library (libpulse.so.0) found", NULL);
        log_line("dlopen: %s", error);
        return -1;
    }
    loop = p_pa_threaded_mainloop_new();
    context = p_pa_context_new(p_pa_threaded_mainloop_get_api(loop), "R.A.T. tablet PC Audio");
    p_pa_context_set_state_callback(context, on_state, NULL);
    p_pa_threaded_mainloop_lock(loop);
    p_pa_threaded_mainloop_start(loop);
    int state = PA_CONTEXT_FAILED;
    if (p_pa_context_connect(context, NULL, 0, NULL) >= 0)
        while ((state = p_pa_context_get_state(context)) != PA_CONTEXT_READY && state != PA_CONTEXT_FAILED &&
               state != PA_CONTEXT_TERMINATED)
            p_pa_threaded_mainloop_wait(loop);
    if (state != PA_CONTEXT_READY)
    {
        line("error", "No audio server (PulseAudio or PipeWire) found", NULL);
        log_line("connect: %s", p_pa_strerror(p_pa_context_errno(context)));
        p_pa_threaded_mainloop_unlock(loop);
        return -1;
    }
    return 0;
}

static void disconnect_server(void)
{
    p_pa_context_disconnect(context);
    p_pa_context_unref(context);
    p_pa_threaded_mainloop_unlock(loop);
    p_pa_threaded_mainloop_stop(loop);
    p_pa_threaded_mainloop_free(loop);
}

typedef struct
{
    char name[256];
    char version[128];
    char default_sink[256];
} server_t;

static void on_server(pa_context *c, const pa_server_info *info, void *userdata)
{
    (void)c;
    server_t *server = userdata;
    if (info)
    {
        snprintf(server->name, sizeof server->name, "%s", info->server_name ? info->server_name : "");
        snprintf(server->version, sizeof server->version, "%s", info->server_version ? info->server_version : "");
        snprintf(server->default_sink, sizeof server->default_sink, "%s", info->default_sink_name ? info->default_sink_name : "");
    }
    signal_loop();
}

static void read_server(server_t *server)
{
    memset(server, 0, sizeof *server);
    finish(p_pa_context_get_server_info(context, on_server, server));
}

typedef struct
{
    uint32_t index;
    uint32_t sink;
    pa_cvolume volume;
    char binary[128];
    char name[128];
    unsigned pid;
    int corked;
} stream_t;

typedef struct
{
    stream_t items[MAX_STREAMS];
    int count;
    const char *program;   // NULL for all streams
} streams_t;

static const char *prop(const pa_sink_input_info *info, const char *key)
{
    const char *value = info->proplist ? p_pa_proplist_gets(info->proplist, key) : NULL;
    return value ? value : "";
}

// Matches binary or name, case-insensitively.
static int is_program(const char *program, const char *binary, const char *name)
{
    return strcasecmp(program, binary) == 0 || strcasecmp(program, name) == 0;
}

static void on_stream(pa_context *c, const pa_sink_input_info *info, int eol, void *userdata)
{
    (void)c;
    streams_t *streams = userdata;
    if (eol || !info)
    {
        signal_loop();
        return;
    }
    const char *binary = prop(info, "application.process.binary");
    const char *name = prop(info, "application.name");
    // The helper's own streams (a test tone) are never a program.
    if (strcmp(name, "R.A.T. tablet PC Audio") == 0 && strcmp(binary, "ratcapture-linux") == 0) return;
    if (streams->program && !is_program(streams->program, binary, name)) return;
    if (streams->count == MAX_STREAMS) return;
    stream_t *s = &streams->items[streams->count++];
    s->index = info->index;
    s->sink = info->sink;
    s->volume = info->volume;
    s->corked = info->corked;
    snprintf(s->binary, sizeof s->binary, "%s", binary);
    snprintf(s->name, sizeof s->name, "%s", name);
    s->pid = (unsigned)strtoul(prop(info, "application.process.id"), NULL, 10);
}

static void read_streams(streams_t *streams, const char *program)
{
    memset(streams, 0, sizeof *streams);
    streams->program = program;
    finish(p_pa_context_get_sink_input_info_list(context, on_stream, streams));
}

static void on_one_stream(pa_context *c, const pa_sink_input_info *info, int eol, void *userdata)
{
    (void)c;
    if (!eol && info) *(int *)userdata = 1;
    signal_loop();
}

static int stream_exists(uint32_t index)
{
    int found = 0;
    finish(p_pa_context_get_sink_input_info(context, index, on_one_stream, &found));
    return found;
}

static void on_loaded(pa_context *c, uint32_t index, void *userdata)
{
    (void)c;
    *(uint32_t *)userdata = index;
    signal_loop();
}

typedef struct
{
    uint32_t modules[8];
    int count;
} modules_t;

static void on_module(pa_context *c, const pa_module_info *info, int eol, void *userdata)
{
    (void)c;
    modules_t *found = userdata;
    if (eol || !info)
    {
        signal_loop();
        return;
    }
    if (info->name && strcmp(info->name, "module-null-sink") == 0 && info->argument &&
        strstr(info->argument, "sink_name=" SINK_NAME) && found->count < 8)
        found->modules[found->count++] = info->index;
}

// The server moves what still plays there to the default sink.
static int unload_sinks(void)
{
    modules_t found = { { 0 }, 0 };
    finish(p_pa_context_get_module_info_list(context, on_module, &found));
    for (int i = 0; i < found.count; i++)
        finish(p_pa_context_unload_module(context, found.modules[i], on_success, NULL));
    return found.count;
}

static uint32_t load_sink(int rate)
{
    char arguments[256];
    snprintf(arguments, sizeof arguments,
        "sink_name=" SINK_NAME " rate=%d channels=2 sink_properties=device.description=R.A.T.-PC-Audio", rate);
    uint32_t module = PA_INVALID_INDEX;
    finish(p_pa_context_load_module(context, "module-null-sink", arguments, on_loaded, &module));
    return module;
}

// WirePlumber can make the new sink the default, so everything would play into the capture. Puts the old default back.
static void keep_default(const char *speakers)
{
    server_t server;
    read_server(&server);
    if (strcmp(server.default_sink, SINK_NAME) != 0 || !speakers[0] || strcmp(speakers, SINK_NAME) == 0) return;
    finish(p_pa_context_set_default_sink(context, speakers, on_success, NULL));
    log_line("The audio server made the capture sink the default; set %s back", speakers);
}

typedef struct
{
    uint32_t index;
    uint32_t sink;
    char sink_name[256];
    pa_cvolume volume;
} moved_t;

static moved_t moved[MAX_STREAMS];
static int moved_count;
static char restore_path[PATH_MAX];

// Next to the helper, so every run and the cleanup share it.
static void find_restore_path(void)
{
    char exe[PATH_MAX - 32];
    ssize_t length = readlink("/proc/self/exe", exe, sizeof exe - 1);
    if (length <= 0) { snprintf(restore_path, sizeof restore_path, "/tmp/ratcapture-restore.txt"); return; }
    exe[length] = 0;
    char *slash = strrchr(exe, '/');
    if (slash) *slash = 0;
    snprintf(restore_path, sizeof restore_path, "%s/restore-linux.txt", exe);
}

// One line per moved stream: index, sink name, channel volumes. Written before the move, so a crash leaves a way back.
static void write_restore_file(void)
{
    if (moved_count == 0)
    {
        unlink(restore_path);
        return;
    }
    FILE *file = fopen(restore_path, "w");
    if (!file) return;
    for (int i = 0; i < moved_count; i++)
    {
        fprintf(file, "%u\t%s\t%u", moved[i].index, moved[i].sink_name, moved[i].volume.channels);
        for (int c = 0; c < moved[i].volume.channels; c++) fprintf(file, "\t%u", moved[i].volume.values[c]);
        fputc('\n', file);
    }
    fclose(file);
}

typedef struct
{
    char *name;
    size_t size;
} name_out_t;

static void on_sink(pa_context *c, const pa_sink_info *info, int eol, void *userdata)
{
    (void)c;
    name_out_t *out = userdata;
    if (!eol && info && info->name) snprintf(out->name, out->size, "%s", info->name);
    signal_loop();
}

// Empty when the sink is gone. Names outlive indexes across server restarts.
static void sink_name_of(uint32_t sink, char *name, size_t size)
{
    name[0] = 0;
    name_out_t out = { name, size };
    finish(p_pa_context_get_sink_info_by_index(context, sink, on_sink, &out));
}

// Unloads rat_capture first, which moves its streams to the default sink. Returns the streams put back.
static int restore_from_file(void)
{
    int unloaded = unload_sinks();
    FILE *file = fopen(restore_path, "r");
    int restored = 0;
    if (file)
    {
        char text[1024];
        while (fgets(text, sizeof text, file))
        {
            moved_t entry;
            memset(&entry, 0, sizeof entry);
            char *field = strtok(text, "\t\n");
            if (!field) continue;
            entry.index = (uint32_t)strtoul(field, NULL, 10);
            if (!(field = strtok(NULL, "\t\n"))) continue;
            snprintf(entry.sink_name, sizeof entry.sink_name, "%s", field);
            if (!(field = strtok(NULL, "\t\n"))) continue;
            entry.volume.channels = (uint8_t)atoi(field);
            if (entry.volume.channels > PA_CHANNELS_MAX) continue;
            int ok = 1;
            for (int c = 0; c < entry.volume.channels; c++)
            {
                if (!(field = strtok(NULL, "\t\n"))) { ok = 0; break; }
                entry.volume.values[c] = (pa_volume_t)strtoul(field, NULL, 10);
            }
            if (!ok || !stream_exists(entry.index)) continue;
            finish(p_pa_context_move_sink_input_by_name(context, entry.index, entry.sink_name, on_success, NULL));
            finish(p_pa_context_set_sink_input_volume(context, entry.index, &entry.volume, on_success, NULL));
            restored++;
        }
        fclose(file);
        unlink(restore_path);
    }
    if (unloaded || restored)
        log_line("Put back what a helper that ended early left: %d capture sink(s) removed, %d stream(s) restored",
            unloaded, restored);
    return restored;
}

static int is_moved(uint32_t index)
{
    for (int i = 0; i < moved_count; i++)
        if (moved[i].index == index) return 1;
    return 0;
}

// Moves at full volume, as the stream's own volume would make the tablet quieter. Saves old sink and volume first.
static void quiet_streams(const char *program)
{
    streams_t streams;
    read_streams(&streams, program);
    int fresh = 0;
    for (int i = 0; i < streams.count && moved_count < MAX_STREAMS; i++)
    {
        stream_t *s = &streams.items[i];
        if (is_moved(s->index)) continue;
        moved_t *m = &moved[moved_count++];
        m->index = s->index;
        m->sink = s->sink;
        m->volume = s->volume;
        sink_name_of(s->sink, m->sink_name, sizeof m->sink_name);
        fresh++;
    }
    if (!fresh) return;
    write_restore_file();
    for (int i = moved_count - fresh; i < moved_count; i++)
    {
        pa_cvolume full = moved[i].volume;
        for (int c = 0; c < full.channels; c++) full.values[c] = PA_VOLUME_NORM;
        finish(p_pa_context_move_sink_input_by_name(context, moved[i].index, SINK_NAME, on_success, NULL));
        finish(p_pa_context_set_sink_input_volume(context, moved[i].index, &full, on_success, NULL));
    }
}

// Falls back to the current default sink when the old one is gone.
static void unquiet_streams(void)
{
    for (int i = 0; i < moved_count; i++)
    {
        moved_t *m = &moved[i];
        if (!stream_exists(m->index)) continue;
        int back = 0;
        finish(p_pa_context_move_sink_input_by_index(context, m->index, m->sink, on_success, &back));
        if (!back)
        {
            server_t server;
            read_server(&server);
            if (server.default_sink[0] && strcmp(server.default_sink, SINK_NAME) != 0)
                finish(p_pa_context_move_sink_input_by_name(context, m->index, server.default_sink, on_success, NULL));
        }
        finish(p_pa_context_set_sink_input_volume(context, m->index, &m->volume, on_success, NULL));
    }
    moved_count = 0;
    write_restore_file();
}

static float level_peak;
// When "quiet on" moved the program here. The delay to the first sound is logged.
static volatile long quiet_since = -1;
// When the play key went to the program. The delay to the first sound is logged.
static volatile long play_since = -1;

static void on_read(pa_stream *stream, size_t nbytes, void *userdata)
{
    (void)nbytes; (void)userdata;
    const void *data;
    size_t length;
    static char silence[16384];
    while (p_pa_stream_peek(stream, &data, &length) == 0 && length > 0)
    {
        if (data)
        {
            const float *samples = data;
            float peak = 0;
            for (size_t i = 0; i < length / sizeof(float); i++)
                if (fabsf(samples[i]) > peak) peak = fabsf(samples[i]);
            if (peak > level_peak) level_peak = peak;
            if (quiet_since >= 0 && peak > 0)
            {
                log_line("Sound arrived %ld ms after the program was moved", now_ms() - quiet_since);
                quiet_since = -1;
            }
            if (play_since >= 0 && peak > 0)
            {
                log_line("Sound arrived %ld ms after the play key", now_ms() - play_since);
                play_since = -1;
            }
            emit('A', data, length);
        }
        else
            // A hole: fill with silence.
            for (size_t done = 0; done < length; done += sizeof silence)
                emit('A', silence, length - done < sizeof silence ? length - done : sizeof silence);
        p_pa_stream_drop(stream);
    }
}

static pa_stream *start_recording(int rate)
{
    pa_sample_spec spec = { PA_SAMPLE_FLOAT32LE, (uint32_t)rate, CHANNELS };
    // About 20 ms per read.
    pa_buffer_attr attr = { (uint32_t)-1, (uint32_t)-1, (uint32_t)-1, (uint32_t)-1, (uint32_t)(rate * CHANNELS * 4 / 50) };
    pa_stream *stream = p_pa_stream_new(context, "R.A.T. tablet PC Audio", &spec, NULL);
    p_pa_stream_set_state_callback(stream, on_stream_state, NULL);
    p_pa_stream_set_read_callback(stream, on_read, NULL);
    p_pa_stream_connect_record(stream, SINK_NAME ".monitor", &attr, PA_STREAM_ADJUST_LATENCY);
    int state;
    while ((state = p_pa_stream_get_state(stream)) != PA_STREAM_READY && state != PA_STREAM_FAILED &&
           state != PA_STREAM_TERMINATED)
        p_pa_threaded_mainloop_wait(loop);
    if (state == PA_STREAM_READY) return stream;
    p_pa_stream_unref(stream);
    return NULL;
}

static int have_mpris;

// Matched by the streams' binary, name and process. Returns 0 when found.
static int find_player(const char *program, mpris_player *player)
{
    if (!have_mpris) return -1;
    streams_t streams;
    read_streams(&streams, program);
    const char *binary = streams.count ? streams.items[0].binary : program;
    const char *name = streams.count ? streams.items[0].name : program;
    unsigned pid = streams.count ? streams.items[0].pid : 0;
    return mpris_find(binary, name, pid, player);
}

// Empty without a player.
static void media_status(const char *program, char *status, size_t size)
{
    status[0] = 0;
    mpris_player player;
    mpris_now now;
    if (find_player(program, &player) == 0 && mpris_now_playing(&player, &now) == 0)
        snprintf(status, size, "%s", now.status);
}

static int send_key(const char *program, const char *method)
{
    mpris_player player;
    return find_player(program, &player) == 0 ? mpris_key(&player, method) : -1;
}

static int sleep_unlocked(int ms)
{
    p_pa_threaded_mainloop_unlock(loop);
    struct timespec wait = { ms / 1000, (ms % 1000) * 1000000L };
    nanosleep(&wait, NULL);
    p_pa_threaded_mainloop_lock(loop);
    return 0;
}

// Returns at once without a player.
static void pause_and_wait(const char *program)
{
    char status[32];
    media_status(program, status, sizeof status);
    if (status[0] == 0) return;
    // A player can report "Paused" while it plays again, and pausing a paused player does nothing.
    send_key(program, "Pause");
    log_line("Pausing %s before giving its volume back (it reported %s)", program, status);
    long until = now_ms() + PAUSE_TIMEOUT_MS;
    do
    {
        sleep_unlocked(100);
        media_status(program, status, sizeof status);
    } while (strcmp(status, "Playing") == 0 && now_ms() < until);
    if (moved_count) sleep_unlocked(PAUSED_SETTLE_MS);
}

typedef struct
{
    char text[4096];
    size_t used;
} input_t;

// Returns one command line at a time, -1 once the other side has closed.
static int next_command(int fd, input_t *in, char *command, size_t size, int timeout_ms)
{
    for (;;)
    {
        char *newline = memchr(in->text, '\n', in->used);
        if (newline)
        {
            size_t length = (size_t)(newline - in->text);
            snprintf(command, size, "%.*s", (int)length, in->text);
            if (length && command[length - 1] == '\r') command[length - 1] = 0;
            memmove(in->text, newline + 1, in->used - length - 1);
            in->used -= length + 1;
            return 1;
        }
        struct pollfd wait = { fd, POLLIN, 0 };
        p_pa_threaded_mainloop_unlock(loop);
        int ready = poll(&wait, 1, timeout_ms);
        p_pa_threaded_mainloop_lock(loop);
        if (ready <= 0) return 0;
        ssize_t got = fd == sock ? recv(fd, in->text + in->used, sizeof in->text - in->used - 1, 0)
                                 : read(fd, in->text + in->used, sizeof in->text - in->used - 1);
        if (got <= 0) return -1;
        in->used += (size_t)got;
        if (in->used >= sizeof in->text - 1) in->used = 0;
        timeout_ms = 0;
    }
}

static int run(const char *program, int rate)
{
    have_mpris = 0;
    const char *why;
    if (mpris_open(&why) == 0) have_mpris = 1;
    else log_line("No song titles or keys: %s", why);

    restore_from_file();
    server_t server;
    read_server(&server);
    char speakers[256];
    snprintf(speakers, sizeof speakers, "%s", server.default_sink);
    log_line("Audio server: %s %s, the PC plays to %s", server.name, server.version, speakers);

    uint32_t module = load_sink(rate);
    if (module == PA_INVALID_INDEX)
    {
        line("error", "The audio server refused the capture sink (module-null-sink)", NULL);
        return 1;
    }
    keep_default(speakers);
    pa_stream *recording = start_recording(rate);
    if (!recording)
    {
        line("error", "The audio server refused the recording of the capture sink", NULL);
        finish(p_pa_context_unload_module(context, module, on_success, NULL));
        return 1;
    }

    int fd = sock >= 0 ? sock : 0;
    input_t input = { { 0 }, 0 };
    char command[256];
    int quiet = 0, waiting = -1, quiet_off_pending = 0;
    long quiet_off_deadline = 0, paused_since = -1, next_housekeeping = 0, next_level = now_ms() + LEVEL_REPORT_MS;
    char last_media[1024] = "";

    while (!output_gone)
    {
        int got = next_command(fd, &input, command, sizeof command, 100);
        if (got < 0) break;
        if (got > 0)
        {
            if (strcmp(command, "quiet on") == 0)
            {
                if (!quiet) quiet_since = now_ms();
                quiet = 1;
                quiet_off_pending = 0;
                quiet_streams(program);
            }
            else if (strcmp(command, "quiet off") == 0)
            {
                char status[32];
                media_status(program, status, sizeof status);
                if (quiet && strcmp(status, "Playing") == 0)
                {
                    // Usually right after "pause", before the program has stopped.
                    quiet_off_pending = 1;
                    quiet_off_deadline = now_ms() + PAUSE_TIMEOUT_MS;
                    paused_since = -1;
                }
                else
                {
                    quiet = 0;
                    unquiet_streams();
                }
            }
            else if (strcmp(command, "play") == 0 || strcmp(command, "pause") == 0 || strcmp(command, "next") == 0 ||
                     strcmp(command, "previous") == 0)
            {
                const char *method = strcmp(command, "play") == 0 ? "Play" : strcmp(command, "pause") == 0 ? "Pause"
                                   : strcmp(command, "next") == 0 ? "Next" : "Previous";
                // Timed only from a stop, as a playing program ignores the key.
                char status[32] = "";
                if (strcmp(method, "Play") == 0) media_status(program, status, sizeof status);
                long sent = now_ms();
                if (send_key(program, method) != 0) log_line("%s did not take '%s'", program, command);
                else if (strcmp(method, "Play") == 0 && strcmp(status, "Playing") != 0) play_since = sent;
            }
            else if (command[0])
                log_line("Unknown command '%s'", command);
            continue;
        }

        long now = now_ms();
        if (quiet_off_pending)
        {
            char status[32];
            media_status(program, status, sizeof status);
            if (strcmp(status, "Playing") == 0)
            {
                paused_since = -1;
                if (now >= quiet_off_deadline)
                {
                    quiet_off_pending = 0;
                    log_line("%s kept playing; it stays silent on the PC", program);
                }
            }
            else
            {
                if (paused_since < 0) paused_since = now;
                if (now - paused_since >= PAUSED_SETTLE_MS)
                {
                    quiet_off_pending = 0;
                    quiet = 0;
                    unquiet_streams();
                }
            }
        }
        if (now < next_housekeeping) continue;
        next_housekeeping = now + HOUSEKEEPING_MS;

        streams_t streams;
        read_streams(&streams, program);
        mpris_player player;
        int has_player = find_player(program, &player) == 0;
        int present = streams.count > 0 || has_player;
        if (present && waiting != 0) line("capturing", NULL);
        if (!present && waiting != 1) line("waiting", NULL);
        waiting = !present;
        if (quiet) quiet_streams(program);
        keep_default(speakers);

        char media[1024];
        mpris_now playing;
        if (has_player && mpris_now_playing(&player, &playing) == 0)
            snprintf(media, sizeof media, "media\t%s\t%s\t%s", playing.status, playing.artist, playing.title);
        else
            snprintf(media, sizeof media, "nomedia");
        if (strcmp(media, last_media) != 0)
        {
            snprintf(last_media, sizeof last_media, "%s", media);
            if (has_player && strncmp(media, "media", 5) == 0)
                line("media", playing.status, playing.artist, playing.title, NULL);
            else
                line("nomedia", NULL);
        }

        if (now >= next_level)
        {
            next_level = now + LEVEL_REPORT_MS;
            if (quiet) log_line("Level: peak %.3f over the last %d s", level_peak, LEVEL_REPORT_MS / 1000);
            level_peak = 0;
        }
    }

    // Pause first, so the program does not go on loud on the PC.
    pause_and_wait(program);
    unquiet_streams();
    p_pa_stream_disconnect(recording);
    p_pa_stream_unref(recording);
    finish(p_pa_context_unload_module(context, module, on_success, NULL));
    return 0;
}

static int list(void)
{
    int mpris = 0;
    const char *why;
    if (mpris_open(&why) == 0) mpris = 1;
    streams_t streams;
    read_streams(&streams, NULL);
    for (int i = 0; i < streams.count; i++)
    {
        stream_t *s = &streams.items[i];
        const char *program = s->binary[0] ? s->binary : s->name;
        if (!program[0]) continue;
        int seen = 0;
        for (int j = 0; j < i; j++)
        {
            const char *other = streams.items[j].binary[0] ? streams.items[j].binary : streams.items[j].name;
            if (strcasecmp(other, program) == 0) seen = 1;
        }
        if (seen) continue;
        mpris_player player;
        int media = mpris && mpris_find(s->binary, s->name, s->pid, &player) == 0;
        char pid[16];
        snprintf(pid, sizeof pid, "%u", s->pid);
        line("app", pid, program, s->name[0] ? s->name : program, media ? "1" : "0", NULL);
    }
    return 0;
}

static void diag_line(const char *format, ...)
{
    char text[1024];
    va_list args;
    va_start(args, format);
    vsnprintf(text, sizeof text, format, args);
    va_end(args);
    if (sock >= 0) line("log", text, NULL);
    else printf("%s\n", text);
}

static int diag(void)
{
    diag_line("ratcapture-linux diag, glibc %s", gnu_get_libc_version());
    diag_line("Container: %s", access("/run/host", F_OK) == 0 ? "Steam Linux Runtime (pressure-vessel)"
        : access("/.flatpak-info", F_OK) == 0 ? "Flatpak" : "none");
    diag_line("XDG_RUNTIME_DIR=%s PULSE_SERVER=%s DBUS_SESSION_BUS_ADDRESS=%s", getenv("XDG_RUNTIME_DIR") ?: "(unset)",
        getenv("PULSE_SERVER") ?: "(unset)", getenv("DBUS_SESSION_BUS_ADDRESS") ?: "(unset)");
    server_t server;
    read_server(&server);
    diag_line("Audio server: %s %s, default sink %s", server.name, server.version, server.default_sink);
    modules_t found = { { 0 }, 0 };
    finish(p_pa_context_get_module_info_list(context, on_module, &found));
    diag_line("Capture sinks left loaded: %d; restore file %s", found.count,
        access(restore_path, F_OK) == 0 ? "present" : "none");
    streams_t streams;
    read_streams(&streams, NULL);
    for (int i = 0; i < streams.count; i++)
        diag_line("Stream %u: binary '%s', name '%s', pid %u, sink %u, volume %.0f%%%s", streams.items[i].index,
            streams.items[i].binary, streams.items[i].name, streams.items[i].pid, streams.items[i].sink,
            streams.items[i].volume.channels ? 100.0 * streams.items[i].volume.values[0] / PA_VOLUME_NORM : 0.0,
            streams.items[i].corked ? ", paused" : "");
    if (streams.count == 0) diag_line("No program plays sound");
    const char *why;
    if (mpris_open(&why) != 0)
        diag_line("MPRIS: %s", why);
    else
    {
        mpris_player players[32];
        int count = mpris_players(players, 32);
        for (int i = 0; i < count; i++)
            diag_line("Player %s: identity '%s', pid %u", players[i].bus_name, players[i].identity, players[i].pid);
        if (count == 0) diag_line("MPRIS: no player on the session bus");
    }
    return 0;
}

static long tone_frame;

static void on_write(pa_stream *stream, size_t nbytes, void *userdata)
{
    (void)userdata;
    // The server asks again only after it got all it asked for.
    static float block[4096];
    size_t left = nbytes / (sizeof(float) * CHANNELS);
    while (left > 0)
    {
        size_t frames = left < sizeof block / sizeof block[0] / CHANNELS ? left : sizeof block / sizeof block[0] / CHANNELS;
        for (size_t i = 0; i < frames; i++, tone_frame++)
            block[2 * i] = block[2 * i + 1] = (float)(0.5 * sin(2 * M_PI * 440 * tone_frame / 48000.0));
        if (p_pa_stream_write(stream, block, frames * sizeof(float) * CHANNELS, NULL, 0, 0) != 0) return;
        left -= frames;
    }
}

static int tone(int seconds)
{
    pa_sample_spec spec = { PA_SAMPLE_FLOAT32LE, 48000, CHANNELS };
    pa_stream *stream = p_pa_stream_new(context, "test tone", &spec, NULL);
    p_pa_stream_set_state_callback(stream, on_stream_state, NULL);
    p_pa_stream_set_write_callback(stream, on_write, NULL);
    p_pa_stream_connect_playback(stream, NULL, NULL, 0, NULL, NULL);
    for (int second = 0; second < seconds; second++) sleep_unlocked(1000);
    p_pa_stream_disconnect(stream);
    p_pa_stream_unref(stream);
    return 0;
}

static int test_sink(const char *name)
{
    char arguments[256];
    snprintf(arguments, sizeof arguments, "sink_name=%s rate=48000 channels=2", name);
    uint32_t module = PA_INVALID_INDEX;
    finish(p_pa_context_load_module(context, "module-null-sink", arguments, on_loaded, &module));
    finish(p_pa_context_set_default_sink(context, name, on_success, NULL));
    printf("Loaded %s (module %u) as the default sink\n", name, module);
    return 0;
}

int main(int argc, char **argv)
{
    signal(SIGPIPE, SIG_IGN);
    if (argc < 2)
    {
        fprintf(stderr, "Use: ratcapture-linux run <program> <rate> [port] | list [port] | cleanup [port] | diag [port]\n");
        return 2;
    }
    const char *command = argv[1];
    const char *port = strcmp(command, "run") == 0 ? (argc > 4 ? argv[4] : NULL)
        : strcmp(command, "list") == 0 || strcmp(command, "cleanup") == 0 || strcmp(command, "diag") == 0
            ? (argc > 2 ? argv[2] : NULL) : NULL;
    if (port && connect_mod(port) != 0) return 1;
    terminal_lines_to_stderr = strcmp(command, "run") == 0;
    find_restore_path();
    if (connect_server() != 0) return 1;

    int result = 2;
    if (strcmp(command, "run") == 0 && argc >= 4)
        result = run(argv[2], atoi(argv[3]));
    else if (strcmp(command, "list") == 0)
        result = list();
    else if (strcmp(command, "cleanup") == 0)
    {
        int restored = restore_from_file();
        if (sock < 0) printf("Restored %d stream(s); no capture sink is left loaded\n", restored);
        result = 0;
    }
    else if (strcmp(command, "diag") == 0)
        result = diag();
    else if (strcmp(command, "tone") == 0 && argc == 3)
        result = tone(atoi(argv[2]));
    else if (strcmp(command, "test-sink") == 0 && argc == 3)
        result = test_sink(argv[2]);
    disconnect_server();
    if (sock >= 0) close(sock);
    return result;
}
