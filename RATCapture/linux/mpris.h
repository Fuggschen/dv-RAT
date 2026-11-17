// MPRIS player status and keys over the D-Bus session bus. libdbus-1.so.3 is loaded at run time.

#ifndef RAT_MPRIS_H
#define RAT_MPRIS_H

typedef struct
{
    char bus_name[256];   // e.g. "org.mpris.MediaPlayer2.spotify"
    char identity[128];   // e.g. "Spotify"
    unsigned pid;         // 0 when the bus does not say
} mpris_player;

typedef struct
{
    char status[32];      // "Playing", "Paused" or "Stopped"
    char artist[256];     // first artist, may be empty
    char title[256];
} mpris_now;

// Returns 0, or -1 with the reason in *error (no libdbus, no bus).
int mpris_open(const char **error);

// Returns how many players were written, at most max.
int mpris_players(mpris_player *players, int max);

// Matches bus name or identity against binary or name, or the owner process against pid. Returns 0, or -1 if none.
int mpris_find(const char *binary, const char *name, unsigned pid, mpris_player *player);

// Returns 0, or -1 when the player did not answer.
int mpris_now_playing(const mpris_player *player, mpris_now *now);

// method is "Play", "Pause", "Next" or "Previous". Returns 0, or -1 when refused.
int mpris_key(const mpris_player *player, const char *method);

#endif
