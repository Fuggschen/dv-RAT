# Route Assistant Tablet (R.A.T.)

A Derail Valley mod that adds an old but reliable tablet you mount on your train. It shows your train, the route
ahead as an EBuLa-inspired timetable, your jobs with a shunting plan, the station's yard, the weather, and
plays music through internal speakers or those you mount on the train.

The tablet behaves like old hardware. It boots slowly and you hear its hard disk spinning up,
hums and clicks while it works, and its pages build up slowly.
Pages that need remote data load over a radio link that depends on the signal strength. Lower signal strength means longer loading times.

## Items

Three items are sold in the shop:

| Item | Price | Stock | What it does |
|---|---|---|---|
| Route Assistant Tablet | $5000 | 5 | The tablet. Mount it on a car. |
| R.A.T. Train Speaker | $3000 | 25 | A loudspeaker. Connects by itself to a tablet on the same train; up to five play together. |
| R.A.T. Train Subwoofer | $6000 | 5 | Plays only the bass. One per train. |

Speakers and the subwoofer light an LED while a tablet is connected. A speaker plays at full volume up
to 2 m and fades to silence at 60 m; the subwoofer's bass carries from 3 m to 100 m. A closed cab muffles the music to the outside.

## Using the tablet

- **Power:** press the red power button on the left.
- **Picking it up:** Only a switched off tablet can be picked up again.
- **VR:** Compatible and tested. Screen works the same as with a mouse.
- **Start key:** the square key with the R.A.T. logo at the bottom left opens the Start menu where you can find every page.
- **Back:** tap the title, or the `<` left of it, to go back.
- **Header:** tap the clock to open the Stopwatch, the weather to open Weather, and the play/pause symbol to open the Media Player when you have music playing. The bars between weather and clock show the radio signal strength.
- **Soft keys:** up to seven keys right of the Start key change with the page. Used to provide extra submenus for each page.

### Crashes

A jolt of the tablet's car of about 2 g (a hard coupling) can crash the tablet. The chance rises with the
jolt until a crash is certain at about 4 g (a collision). A crashed tablet shows a blue screen until you
press the power button.

### Radio signal

The tablet works out its reception the way the game's Boombox does. Pages with remote data (Jobs, Station, Fees, Owned locos, Weather) show a loading bar on every visit:

| Signal | Loading time |
|---|---|
| 3 bars | 1 to 3 s |
| 2 bars | 3 to 5 s |
| 1 bar | 5 to 10 s |
| No bars | Stalls until the signal returns |

The route planner and the internet radio depend on the signal too.

## Pages

### Train

Speed, mass, length, braking distance, grade and altitude. The car list has three views:

- **Cars:** each car with its cargo, job and the next track it goes to. *Group by job* puts neighbouring
  cars of one job that go to the same track into one row.
- **Brakes:** brake pipe, control reservoir, reservoir and cylinder pressure, brake force,
  brake temperature and handbrakes per car. With the Realistic Brakes mod, each car's valve type too.
- **Stress:** mass, how close each car is to derailing, and coupler forces.

Subpages:

- **Locomotive:** controls (throttle, reverser, cutoff, brakes), tractive effort, power, adhesion,
  wheelslip and wheelslide, traction motor heat, and brake pressures. Shows the loco's fee or service
  price.
  - **Supplies:** fuel, oil, sand, water, coal and battery as bars, each with its use per minute and the
    time left. Engine state and temperature; boiler pressure, boiler water and fire for steam locos.
  - **Electric:** for locos on the overhead wire of the 1500 V DC Catenary mod: line voltage, current,
    power, regeneration, energy used, traction motors and each pantograph. The WE-4U's traction battery
    is shown as well.
  - **Damage:** health of body, wheels and powertrain, working traction motors, and the state of each fuse.
- **Power:** with the Solar Gadgets mod, each car's charge, charge rate, supply and demand, its batteries
  and its consumers.

### Route

The route ahead in an EBuLa-inspired timetable. Like the real device it reads bottom to top: a diamond on
the left edge marks the train, the route ahead lies above it and the last passed entries below it. Each
row has the speed limit, distance, place, estimated arrival and grade. Rows show stations and the station
track the switches lead onto, switches, signals, speed and grade changes, and the end of track.

Above the timetable: the train's job number, current track, delay against the job's time limit, length,
mass, next station, speed limit, grade and destination.

Speed limits are worked out from the game's own sign rules, so the tablet knows the limits of track you
have not seen yet. Options on the Settings page add:

- the aspect each signal shows (with the DV Signals mod),
- a `¥` row where the end of the train leaves the last lower limit, so the whole train may speed up,
- switches set against the planned route in red,
- damaged and locked switches (with the Junction Maintenance mod),
- damaged track as a speed restriction (with the Track Maintenance mod).

**Planner** (subpage): the shortest drivable route to a station, through stops you add on the way, with
every switch that must be set and every place the train reverses. With the Dispatcher license you can
throw those switches from the tablet. A longer route and a weaker signal
make the calculation slower. *Wire only* keeps the route under the overhead wire of the 1500 V DC
Catenary mod. With the Passenger Jobs mod the planner offers its rural stations.

### Jobs

Your jobs in tabs: **Accepted**, **Coupled** to the train, **Active**, and **Plan**. Each job has a
toggle that marks it active.

The Plan tab works out the order of shunting moves that finishes the active jobs with the fewest moves:
which cars to couple and uncouple on which track. It collects every job at a station before driving on,
visits the stations in the shortest order, and groups cars for the same track into one block. While you
work through the plan the tablet ticks off each step it sees done, and warns when the train no longer
matches the plan.

Jobs marked on the Station page before you take them are planned like taken ones.

### Station

The jobs the nearest station offers, or a station you pick from the list. Sort by type, origin,
destination, length, weight, pay or pay per minute. Filter by job type, destination and the licenses you
hold. Jobs you lack a license for can be shown in red. Tap a job for its details: route, cars, pay,
licenses and tasks. Mark jobs for the shunting planner from here.

Without the Persistent Jobs mod you see a station's jobs only while you are at it. With that mod the
tablet shows them from anywhere.

- **Yard:** every track of the station as a bar, grouped by sub-yard, with its cars where they stand and
  the free length. Your own train, cars of planned jobs and other trains' locos have their own colors.
  Tap a track for its cars and their cargo and jobs; tap a car to open its job.
  - **Maintenance:** damaged switches (Junction Maintenance mod) and damaged track sections with their
    advised speed (Track Maintenance mod).

### My Computer

Game time, real time, weather, wet rails, money, fees, licenses and owned vehicles.

- **Licenses:** the licenses you currently have.
- **Fees:** every fee you owe, with the total.
- **Owned locos:** your locos with their service price, track and distance.
- **Weather:** the weather now and the game's forecast for the day in 3-hour slots, with the icons of the
  forecast posters at the stations. With the DVSeasons mod: temperature, season and snow cover.
- **Stopwatch:** Start, Stop and Reset. It keeps running on other pages until the tablet is switched off.
- **Reference:** info pages generated from the game.
  - **Stations:** what each station ships and receives, with links to its partner stations.
  - **Cargo:** each cargo, which stations handle it and which cars carry it.
  - **Rolling stock:** what the game's vehicle catalogue shows (masses, value, license chain with prices,
    garage) plus the brake setup and wheel size. Loco pages list the chance that the loco stands at
    each station.
- **Integrations:** which supported mods are loaded and read.
- **Sim ports:** every simulation value of the loco you are in (mostly useful for mod developers).

### Tools

- **Media Player:** plays the music files in your music folder, browsed folder by folder. Keys for Stop,
  Backwards, Play, Forwards, Pause and Shuffle, a volume bar, and a spectrum view. Music keeps playing on
  other pages until the tablet is switched off.
  - **Devices:** the speakers and subwoofer the tablet plays through, each with its own volume and an EQ
    for Lows, Mids and Highs from -12 to +12 dB (the subwoofer has Lows only).
- **Internet Radio:** the game's own stations, your own stations, and the genres and countries of the
  [radio-browser.info](https://www.radio-browser.info) directory. Stations from the directory can be saved
  to your own list. From two signal bars up the stream plays clean; at one bar it stops now and then to
  buffer; with no bars it buffers until the signal returns.
- **PC Audio** (Windows; Linux under Proton or Wine, see below): plays a program running on your PC, such
  as Spotify or a browser, through the tablet. Pick the program from the ones playing sound. Where
  Windows' media controls know the program, the tablet shows the song and has meida keys available. 
  The program is silent on the PC while the tablet plays it and gets its volume back when
  you stop, switch the tablet off or quit the game.

Media Player, Internet Radio and PC Audio share one volume and one set of speakers.

### Settings

- **Rotation:** standard, or inverted for a tablet mounted to the roof.
- **Brightness:** 10 to 100 %.
- **Theme:** Light, Dark, Hotdog, Colorful or Pastel.
- **Buttons:** Simple, Win 2000, Round, Outline or Pill.
- **Route:** the timetable options listed under [Route](#route).

## Supported mods

The tablet reads data from these mods while they are loaded. None of them is required.

| Mod | What the tablet adds |
|---|---|
| 1500 V DC Catenary | Electric page for the 2WE3, wire and neutral sections on the timetable, *Wire only* in the planner |
| Catenary for CCL | Electric page for locos built with it, such as the WE-4U |
| Custom Car Loader | Its locos appear in the loco chances on the Rolling stock pages |
| DV Signals | Signals and their aspects on the timetable |
| DVSeasons | Temperature, season and snow cover on the Weather page and in the forecast |
| Junction Maintenance | Switch damage on the timetable and the Maintenance page |
| Passenger Jobs | Its job types and licenses, its rural stations in the planner and shunting plan |
| Persistent Jobs | Station jobs from anywhere, and the cars it has taken out of far-away yards |
| Realistic Brakes | Valve types, driver's valve and equalizing reservoir |
| Solar Gadgets | The Power page |
| Track Maintenance | Damaged track as speed restrictions, track damage on the Maintenance page |

The Integrations page shows the state of each of these mods except DV Signals and Custom Car Loader: *Active*, *Inactive* (not loaded) or *Failed*
(loaded, but its data could not be read; `Player.log` says why).

## Multiplayer

With the Multiplayer mod, every player needs R.A.T. installed.
A tablet or speaker one player mounts appears on the other players' trains. Every copy of a tablet shows
the same page, and a tap on one shows on all. Music plays from the copy that started it and is streamed
to the others. Speaker volumes and EQ are shared.

## Requirements

- Derail Valley
- Custom Item Mod (`custom_item_mod`)
- Optional: the mods in [Supported mods](#supported-mods), and Multiplayer

## Configuration

- **Music folder:** set in the mod's Unity Mod Manager settings. Empty uses your Windows Music folder.
- **Radio stations:** `radio/stations.json` in the mod folder. See `radio/README.txt` for the format. The
  file is created on first use and never replaced by an update.
- **Pages:** every page is a JSON file in `pages/`. Edit one, switch the tablet off and on, and it is
  rebuilt. See [`DVRAT/ModFiles/pages/README.md`](DVRAT/ModFiles/pages/README.md) for the format, the
  row types and every value a page can show. Problems are listed on the *Content problems* page.
- **Sounds:** the hard disk and shutdown sounds load from `sounds/` in the mod folder and can be swapped
  for other `.wav` files.

## Music formats

Without help the Media Player plays `.mp3`, `.ogg` and `.wav`. The bundled `ffmpeg/ffmpeg.exe` (FFmpeg
7.1.1, LGPL 2.1) adds `.m4a`, `.aac`, `.flac`, `.wma`, `.opus`, `.ape`, `.mka` and `.webm` support.

## PC Audio details

PC Audio uses the bundled `capture/ratcapture.exe`. It records only the chosen program's sound through
Windows' process loopback, which needs Windows 10 version 2004 or later.

If a program stays silent on your PC, for example after the game crashed, double-click
`capture/ratcapture.exe`. It puts every program's volume back.

Under Proton or Wine, `capture/ratcapture-linux` plays a Linux program instead, through PipeWire or
PulseAudio, and reads the song and keys through MPRIS. If a program stays silent after a crash, run
`./ratcapture-linux cleanup` in the `capture` folder.

## Building

The solution is `DVRAT.slnx`:

| Project | Builds |
|---|---|
| `DVRAT` | The mod, `DVRAT.dll` |
| `DVRAT.Multiplayer` | The Multiplayer integration, loaded only when that mod is present |
| `DVRAT.Opus` | Opus encoding for multiplayer sound, with Concentus |
| `RATCapture` | `ratcapture.exe` for PC Audio (.NET Framework 4.8) |
| `DVRAT.Tests` | Unit tests |
| `DVRAT-Unity-Stub` | Stubs of the tablet's components for the Unity project |

1. Set the game, references and Unity paths in `DVRAT/GamePaths.props`.
2. Only when you change the models or prefabs: rebuild the asset bundle in `DV-RAT.UnityProject`
   (Unity 2019.4.40f1) with *Assets > Build AssetBundles*. The built bundle is in the repository.
3. Build the solution. The mod and its files are copied to `build/`.
4. Copy the contents of `build/` into a `DVRAT` folder in Derail Valley's `Mods` folder.

`ffmpeg.exe` is built with `tools/build-ffmpeg.sh` in WSL; without it the Media Player plays only
`.mp3`, `.ogg` and `.wav`. `ratcapture-linux` is built with `tools/build-ratcapture-linux.sh`.
