Your own internet radio stations for the R.A.T. tablet live in stations.json in this folder.
The tablet creates the file the first time you open the radio; updating the mod never replaces it.

Format:

{
  "stations": [
    { "name": "Shown name", "url": "https://example.com/stream" }
  ]
}

"url" is the link to the stream (MP3, AAC or OGG) or to a .pls or .m3u playlist with it.
HLS streams (.m3u8 playlists with #EXT-X- lines) do not play.
Stations you add from the online directory on the tablet are saved here too.
Edit the file while the game runs and reopen the radio page to see the change.
