// Hand-declared subset of libpulse's client API. It mirrors libpulse's stable ABI and is loaded with dlopen,
// so building needs no libpulse-dev.

#ifndef RAT_PULSE_API_H
#define RAT_PULSE_API_H

#include <stddef.h>
#include <stdint.h>

#define PA_CHANNELS_MAX 32
#define PA_VOLUME_NORM 0x10000U
#define PA_INVALID_INDEX ((uint32_t)-1)

typedef struct pa_threaded_mainloop pa_threaded_mainloop;
typedef struct pa_mainloop_api pa_mainloop_api;
typedef struct pa_context pa_context;
typedef struct pa_operation pa_operation;
typedef struct pa_stream pa_stream;
typedef struct pa_proplist pa_proplist;
typedef struct pa_format_info pa_format_info;
typedef uint64_t pa_usec_t;
typedef uint32_t pa_volume_t;

enum { PA_CONTEXT_UNCONNECTED, PA_CONTEXT_CONNECTING, PA_CONTEXT_AUTHORIZING, PA_CONTEXT_SETTING_NAME,
       PA_CONTEXT_READY, PA_CONTEXT_FAILED, PA_CONTEXT_TERMINATED };
enum { PA_OPERATION_RUNNING, PA_OPERATION_DONE, PA_OPERATION_CANCELLED };
enum { PA_STREAM_UNCONNECTED, PA_STREAM_CREATING, PA_STREAM_READY, PA_STREAM_FAILED, PA_STREAM_TERMINATED };
enum { PA_SAMPLE_FLOAT32LE = 5 };
enum { PA_STREAM_ADJUST_LATENCY = 0x2000 };
enum { PA_SUBSCRIPTION_MASK_SINK_INPUT = 0x0004 };
enum { PA_SUBSCRIPTION_EVENT_SINK_INPUT = 0x0002, PA_SUBSCRIPTION_EVENT_FACILITY_MASK = 0x000F,
       PA_SUBSCRIPTION_EVENT_NEW = 0x0000, PA_SUBSCRIPTION_EVENT_TYPE_MASK = 0x0030 };

typedef struct pa_sample_spec { int format; uint32_t rate; uint8_t channels; } pa_sample_spec;
typedef struct pa_channel_map { uint8_t channels; int map[PA_CHANNELS_MAX]; } pa_channel_map;
typedef struct pa_cvolume { uint8_t channels; pa_volume_t values[PA_CHANNELS_MAX]; } pa_cvolume;
typedef struct pa_buffer_attr { uint32_t maxlength, tlength, prebuf, minreq, fragsize; } pa_buffer_attr;

typedef struct pa_sink_input_info
{
    uint32_t index;
    const char *name;
    uint32_t owner_module;
    uint32_t client;
    uint32_t sink;
    pa_sample_spec sample_spec;
    pa_channel_map channel_map;
    pa_cvolume volume;
    pa_usec_t buffer_usec;
    pa_usec_t sink_usec;
    const char *resample_method;
    const char *driver;
    int mute;
    pa_proplist *proplist;
    int corked;
    int has_volume;
    int volume_writable;
    pa_format_info *format;
} pa_sink_input_info;

typedef struct pa_module_info
{
    uint32_t index;
    const char *name;
    const char *argument;
    uint32_t n_used;
    int auto_unload;
    pa_proplist *proplist;
} pa_module_info;

// Only the first two fields. Never allocated or copied here, only read.
typedef struct pa_sink_info
{
    const char *name;
    uint32_t index;
} pa_sink_info;

typedef struct pa_server_info
{
    const char *user_name;
    const char *host_name;
    const char *server_version;
    const char *server_name;
    pa_sample_spec sample_spec;
    const char *default_sink_name;
    const char *default_source_name;
    uint32_t cookie;
    pa_channel_map channel_map;
} pa_server_info;

typedef void (*pa_context_notify_cb_t)(pa_context *c, void *userdata);
typedef void (*pa_context_success_cb_t)(pa_context *c, int success, void *userdata);
typedef void (*pa_context_index_cb_t)(pa_context *c, uint32_t idx, void *userdata);
typedef void (*pa_sink_input_info_cb_t)(pa_context *c, const pa_sink_input_info *i, int eol, void *userdata);
typedef void (*pa_module_info_cb_t)(pa_context *c, const pa_module_info *i, int eol, void *userdata);
typedef void (*pa_server_info_cb_t)(pa_context *c, const pa_server_info *i, void *userdata);
typedef void (*pa_sink_info_cb_t)(pa_context *c, const pa_sink_info *i, int eol, void *userdata);
typedef void (*pa_context_subscribe_cb_t)(pa_context *c, int t, uint32_t idx, void *userdata);
typedef void (*pa_stream_request_cb_t)(pa_stream *p, size_t nbytes, void *userdata);
typedef void (*pa_stream_notify_cb_t)(pa_stream *p, void *userdata);

// Filled by pulse_load.
#define RAT_PULSE_FUNCTIONS(X) \
    X(pa_threaded_mainloop *, pa_threaded_mainloop_new, (void)) \
    X(void, pa_threaded_mainloop_free, (pa_threaded_mainloop *m)) \
    X(int, pa_threaded_mainloop_start, (pa_threaded_mainloop *m)) \
    X(void, pa_threaded_mainloop_stop, (pa_threaded_mainloop *m)) \
    X(void, pa_threaded_mainloop_lock, (pa_threaded_mainloop *m)) \
    X(void, pa_threaded_mainloop_unlock, (pa_threaded_mainloop *m)) \
    X(void, pa_threaded_mainloop_wait, (pa_threaded_mainloop *m)) \
    X(void, pa_threaded_mainloop_signal, (pa_threaded_mainloop *m, int wait_for_accept)) \
    X(pa_mainloop_api *, pa_threaded_mainloop_get_api, (pa_threaded_mainloop *m)) \
    X(pa_context *, pa_context_new, (pa_mainloop_api *api, const char *name)) \
    X(int, pa_context_connect, (pa_context *c, const char *server, int flags, const void *api)) \
    X(void, pa_context_disconnect, (pa_context *c)) \
    X(void, pa_context_unref, (pa_context *c)) \
    X(int, pa_context_get_state, (pa_context *c)) \
    X(int, pa_context_errno, (pa_context *c)) \
    X(void, pa_context_set_state_callback, (pa_context *c, pa_context_notify_cb_t cb, void *userdata)) \
    X(pa_operation *, pa_context_get_server_info, (pa_context *c, pa_server_info_cb_t cb, void *userdata)) \
    X(pa_operation *, pa_context_get_sink_input_info_list, (pa_context *c, pa_sink_input_info_cb_t cb, void *userdata)) \
    X(pa_operation *, pa_context_get_sink_input_info, (pa_context *c, uint32_t idx, pa_sink_input_info_cb_t cb, void *userdata)) \
    X(pa_operation *, pa_context_get_sink_info_by_index, (pa_context *c, uint32_t idx, pa_sink_info_cb_t cb, void *userdata)) \
    X(pa_operation *, pa_context_get_module_info_list, (pa_context *c, pa_module_info_cb_t cb, void *userdata)) \
    X(pa_operation *, pa_context_load_module, (pa_context *c, const char *name, const char *argument, pa_context_index_cb_t cb, void *userdata)) \
    X(pa_operation *, pa_context_unload_module, (pa_context *c, uint32_t idx, pa_context_success_cb_t cb, void *userdata)) \
    X(pa_operation *, pa_context_move_sink_input_by_name, (pa_context *c, uint32_t idx, const char *sink_name, pa_context_success_cb_t cb, void *userdata)) \
    X(pa_operation *, pa_context_move_sink_input_by_index, (pa_context *c, uint32_t idx, uint32_t sink_idx, pa_context_success_cb_t cb, void *userdata)) \
    X(pa_operation *, pa_context_set_default_sink, (pa_context *c, const char *name, pa_context_success_cb_t cb, void *userdata)) \
    X(pa_operation *, pa_context_set_sink_input_volume, (pa_context *c, uint32_t idx, const pa_cvolume *volume, pa_context_success_cb_t cb, void *userdata)) \
    X(pa_operation *, pa_context_subscribe, (pa_context *c, int mask, pa_context_success_cb_t cb, void *userdata)) \
    X(void, pa_context_set_subscribe_callback, (pa_context *c, pa_context_subscribe_cb_t cb, void *userdata)) \
    X(int, pa_operation_get_state, (pa_operation *o)) \
    X(void, pa_operation_unref, (pa_operation *o)) \
    X(const char *, pa_proplist_gets, (pa_proplist *p, const char *key)) \
    X(const char *, pa_strerror, (int error)) \
    X(pa_stream *, pa_stream_new, (pa_context *c, const char *name, const pa_sample_spec *ss, const pa_channel_map *map)) \
    X(int, pa_stream_connect_record, (pa_stream *s, const char *dev, const pa_buffer_attr *attr, int flags)) \
    X(int, pa_stream_get_state, (pa_stream *s)) \
    X(void, pa_stream_set_state_callback, (pa_stream *s, pa_stream_notify_cb_t cb, void *userdata)) \
    X(void, pa_stream_set_read_callback, (pa_stream *s, pa_stream_request_cb_t cb, void *userdata)) \
    X(int, pa_stream_connect_playback, (pa_stream *s, const char *dev, const pa_buffer_attr *attr, int flags, const pa_cvolume *volume, pa_stream *sync))     X(void, pa_stream_set_write_callback, (pa_stream *s, pa_stream_request_cb_t cb, void *userdata))     X(int, pa_stream_write, (pa_stream *s, const void *data, size_t nbytes, void (*free_cb)(void *p), int64_t offset, int seek))     X(int, pa_stream_peek, (pa_stream *s, const void **data, size_t *nbytes)) \
    X(int, pa_stream_drop, (pa_stream *s)) \
    X(int, pa_stream_disconnect, (pa_stream *s)) \
    X(void, pa_stream_unref, (pa_stream *s))

#define RAT_PULSE_DECLARE(ret, name, args) extern ret (*p_##name) args;
RAT_PULSE_FUNCTIONS(RAT_PULSE_DECLARE)
#undef RAT_PULSE_DECLARE

// Returns 0, or -1 with the reason in *error.
int pulse_load(const char **error);

#endif
