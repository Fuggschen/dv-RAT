ratcapture.exe in this folder lets the R.A.T. tablet's PC Audio page play a Windows program (Spotify, a
browser, another player) in the game: it records only that program's sound, through Windows' process
loopback (Windows 10 version 2004 or later), and reads its song and play keys from Windows' media controls.

While the tablet plays a program, the program is turned down to -80 dB on the PC, so you do not hear it twice;
its slider in the Windows volume mixer does not move. The tablet gives the program its volume back when you
stop it there or play other media, switch the tablet off or quit the game. A paused program stays silent.

IF A PROGRAM STAYS SILENT ON THE PC, e.g. after the game crashed: double-click ratcapture.exe. It puts every
program's volume back to normal and says what it changed. The old levels are kept in restore.txt next to it
until they are back.

LINUX (the game under Proton or Wine): ratcapture-linux takes over. It plays the Linux program into an output
of its own, "R.A.T.-PC-Audio", which silences it on the PC, records that output, and reads the song and keys
through MPRIS. It needs PipeWire or PulseAudio, nothing else. If a program stays silent on the PC after a crash,
run ./ratcapture-linux cleanup in this folder. LINUX-TEST.md describes the checks for testers.

Built from the RATCapture folder in the R.A.T. source: ratcapture.exe with .NET Framework 4.8, which comes with
Windows; ratcapture-linux with tools/build-ratcapture-linux.sh (glibc 2.17 or later).
