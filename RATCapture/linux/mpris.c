#include "mpris.h"

#include <dlfcn.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <strings.h>

// Hand-declared subset of libdbus-1, so building needs no libdbus-1-dev. The stand-in structs fit the real sizes (32 and 72 bytes on x86-64).
typedef struct DBusConnection DBusConnection;
typedef struct DBusMessage DBusMessage;
typedef struct { const char *name; const char *message; void *reserved[4]; } DBusError;
typedef struct { void *reserved[16]; } DBusMessageIter;

enum { DBUS_BUS_SESSION = 0 };
enum { DBUS_TYPE_INVALID = 0, DBUS_TYPE_STRING = 's', DBUS_TYPE_UINT32 = 'u', DBUS_TYPE_ARRAY = 'a',
       DBUS_TYPE_VARIANT = 'v', DBUS_TYPE_DICT_ENTRY = 'e' };

#define TIMEOUT_MS 500
#define PREFIX "org.mpris.MediaPlayer2."

#define RAT_DBUS_FUNCTIONS(X) \
    X(void, dbus_error_init, (DBusError *e)) \
    X(void, dbus_error_free, (DBusError *e)) \
    X(unsigned, dbus_error_is_set, (const DBusError *e)) \
    X(DBusConnection *, dbus_bus_get, (int type, DBusError *e)) \
    X(DBusMessage *, dbus_message_new_method_call, (const char *dest, const char *path, const char *iface, const char *method)) \
    X(unsigned, dbus_message_append_args, (DBusMessage *m, int first_type, ...)) \
    X(DBusMessage *, dbus_connection_send_with_reply_and_block, (DBusConnection *c, DBusMessage *m, int timeout, DBusError *e)) \
    X(void, dbus_message_unref, (DBusMessage *m)) \
    X(unsigned, dbus_message_iter_init, (DBusMessage *m, DBusMessageIter *i)) \
    X(int, dbus_message_iter_get_arg_type, (DBusMessageIter *i)) \
    X(void, dbus_message_iter_recurse, (DBusMessageIter *i, DBusMessageIter *sub)) \
    X(unsigned, dbus_message_iter_next, (DBusMessageIter *i)) \
    X(void, dbus_message_iter_get_basic, (DBusMessageIter *i, void *value))

#define RAT_DBUS_DEFINE(ret, name, args) static ret (*p_##name) args;
RAT_DBUS_FUNCTIONS(RAT_DBUS_DEFINE)
#undef RAT_DBUS_DEFINE

static DBusConnection *bus;

int mpris_open(const char **error)
{
    void *library = dlopen("libdbus-1.so.3", RTLD_NOW | RTLD_LOCAL);
    if (!library)
    {
        *error = dlerror();
        return -1;
    }
#define RAT_DBUS_LOAD(ret, name, args) \
    if (!(p_##name = (ret (*) args)dlsym(library, #name))) { *error = "libdbus-1.so.3 lacks " #name; return -1; }
    RAT_DBUS_FUNCTIONS(RAT_DBUS_LOAD)
#undef RAT_DBUS_LOAD

    DBusError e;
    p_dbus_error_init(&e);
    bus = p_dbus_bus_get(DBUS_BUS_SESSION, &e);
    if (!bus)
    {
        static char reason[256];
        snprintf(reason, sizeof reason, "no session bus: %s", p_dbus_error_is_set(&e) && e.message ? e.message : "unknown");
        p_dbus_error_free(&e);
        *error = reason;
        return -1;
    }
    return 0;
}

// Returns the reply, or NULL. The caller unrefs it.
static DBusMessage *call(const char *dest, const char *path, const char *iface, const char *method,
    const char *arg1, const char *arg2)
{
    DBusMessage *message = p_dbus_message_new_method_call(dest, path, iface, method);
    if (!message) return NULL;
    if (arg1 && arg2)
        p_dbus_message_append_args(message, DBUS_TYPE_STRING, &arg1, DBUS_TYPE_STRING, &arg2, DBUS_TYPE_INVALID);
    else if (arg1)
        p_dbus_message_append_args(message, DBUS_TYPE_STRING, &arg1, DBUS_TYPE_INVALID);
    DBusError e;
    p_dbus_error_init(&e);
    DBusMessage *reply = p_dbus_connection_send_with_reply_and_block(bus, message, TIMEOUT_MS, &e);
    p_dbus_message_unref(message);
    if (p_dbus_error_is_set(&e)) p_dbus_error_free(&e);
    return reply;
}

static void copy_string(DBusMessageIter *it, char *out, size_t size)
{
    const char *value = NULL;
    p_dbus_message_iter_get_basic(it, &value);
    snprintf(out, size, "%s", value ? value : "");
}

// The iterator points into the variant and is valid while *reply lives.
static int get_property(const char *dest, const char *iface, const char *property, DBusMessage **reply, DBusMessageIter *value)
{
    *reply = call(dest, "/org/mpris/MediaPlayer2", "org.freedesktop.DBus.Properties", "Get", iface, property);
    DBusMessageIter top;
    if (!*reply || !p_dbus_message_iter_init(*reply, &top) || p_dbus_message_iter_get_arg_type(&top) != DBUS_TYPE_VARIANT)
        return -1;
    p_dbus_message_iter_recurse(&top, value);
    return 0;
}

int mpris_players(mpris_player *players, int max)
{
    if (!bus) return 0;
    DBusMessage *reply = call("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ListNames", NULL, NULL);
    DBusMessageIter top, names;
    if (!reply || !p_dbus_message_iter_init(reply, &top) || p_dbus_message_iter_get_arg_type(&top) != DBUS_TYPE_ARRAY)
    {
        if (reply) p_dbus_message_unref(reply);
        return 0;
    }
    int count = 0;
    p_dbus_message_iter_recurse(&top, &names);
    while (p_dbus_message_iter_get_arg_type(&names) == DBUS_TYPE_STRING && count < max)
    {
        char name[256];
        copy_string(&names, name, sizeof name);
        p_dbus_message_iter_next(&names);
        if (strncmp(name, PREFIX, strlen(PREFIX)) != 0) continue;
        mpris_player *player = &players[count++];
        memset(player, 0, sizeof *player);
        snprintf(player->bus_name, sizeof player->bus_name, "%s", name);

        DBusMessage *identity;
        DBusMessageIter value;
        if (get_property(name, "org.mpris.MediaPlayer2", "Identity", &identity, &value) == 0 &&
            p_dbus_message_iter_get_arg_type(&value) == DBUS_TYPE_STRING)
            copy_string(&value, player->identity, sizeof player->identity);
        if (identity) p_dbus_message_unref(identity);

        DBusMessage *owner = call("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus",
            "GetConnectionUnixProcessID", name, NULL);
        DBusMessageIter pid;
        if (owner && p_dbus_message_iter_init(owner, &pid) && p_dbus_message_iter_get_arg_type(&pid) == DBUS_TYPE_UINT32)
        {
            uint32_t number = 0;
            p_dbus_message_iter_get_basic(&pid, &number);
            player->pid = number;
        }
        if (owner) p_dbus_message_unref(owner);
    }
    p_dbus_message_unref(reply);
    return count;
}

// "spotify" for org.mpris.MediaPlayer2.spotify, "firefox" for ...firefox.instance_1_23.
static void short_name(const char *bus_name, char *out, size_t size)
{
    snprintf(out, size, "%s", bus_name + strlen(PREFIX));
    char *dot = strchr(out, '.');
    if (dot) *dot = 0;
}

int mpris_find(const char *binary, const char *name, unsigned pid, mpris_player *player)
{
    mpris_player players[32];
    int count = mpris_players(players, 32);
    // Process first, then name, as PIDs differ inside Steam's container.
    for (int i = 0; i < count && pid; i++)
        if (players[i].pid == pid) { *player = players[i]; return 0; }
    for (int i = 0; i < count; i++)
    {
        char own[128];
        short_name(players[i].bus_name, own, sizeof own);
        if ((binary[0] && (strcasecmp(own, binary) == 0 || strcasecmp(players[i].identity, binary) == 0)) ||
            (name[0] && (strcasecmp(own, name) == 0 || strcasecmp(players[i].identity, name) == 0)))
        {
            *player = players[i];
            return 0;
        }
    }
    return -1;
}

int mpris_now_playing(const mpris_player *player, mpris_now *now)
{
    memset(now, 0, sizeof *now);
    const char *iface = "org.mpris.MediaPlayer2.Player";
    DBusMessage *reply;
    DBusMessageIter value;
    if (get_property(player->bus_name, iface, "PlaybackStatus", &reply, &value) != 0)
    {
        if (reply) p_dbus_message_unref(reply);
        return -1;
    }
    if (p_dbus_message_iter_get_arg_type(&value) == DBUS_TYPE_STRING)
        copy_string(&value, now->status, sizeof now->status);
    p_dbus_message_unref(reply);

    // Metadata is a{sv}: xesam:artist is a list of strings.
    if (get_property(player->bus_name, iface, "Metadata", &reply, &value) == 0 &&
        p_dbus_message_iter_get_arg_type(&value) == DBUS_TYPE_ARRAY)
    {
        DBusMessageIter entries;
        p_dbus_message_iter_recurse(&value, &entries);
        while (p_dbus_message_iter_get_arg_type(&entries) == DBUS_TYPE_DICT_ENTRY)
        {
            DBusMessageIter entry, field;
            p_dbus_message_iter_recurse(&entries, &entry);
            char key[64];
            copy_string(&entry, key, sizeof key);
            p_dbus_message_iter_next(&entry);
            p_dbus_message_iter_recurse(&entry, &field);
            if (strcmp(key, "xesam:title") == 0 && p_dbus_message_iter_get_arg_type(&field) == DBUS_TYPE_STRING)
                copy_string(&field, now->title, sizeof now->title);
            else if (strcmp(key, "xesam:artist") == 0 && p_dbus_message_iter_get_arg_type(&field) == DBUS_TYPE_ARRAY)
            {
                DBusMessageIter artists;
                p_dbus_message_iter_recurse(&field, &artists);
                if (p_dbus_message_iter_get_arg_type(&artists) == DBUS_TYPE_STRING)
                    copy_string(&artists, now->artist, sizeof now->artist);
            }
            p_dbus_message_iter_next(&entries);
        }
    }
    if (reply) p_dbus_message_unref(reply);
    return 0;
}

int mpris_key(const mpris_player *player, const char *method)
{
    DBusMessage *reply = call(player->bus_name, "/org/mpris/MediaPlayer2", "org.mpris.MediaPlayer2.Player", method, NULL, NULL);
    if (!reply) return -1;
    p_dbus_message_unref(reply);
    return 0;
}
