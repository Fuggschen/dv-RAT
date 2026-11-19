# Tablet pages

Every `.json` file in this folder is one page of the tablet. Edit a file, switch the tablet off
and on with its power button, and the page is rebuilt from the file. Problems (broken JSON, unknown
row types, unknown bindings) are listed on the **Content problems** page in the Start menu and in
the game log (`Player.log`, lines starting with `[DVRAT]`).

Info pages about stations, cargo and rolling stock are generated from the game. To add your own
info page, add a file here like any other page (`text` and `field` rows, `[[...]]` links).

## Page

```json
{
  "id": "train",
  "title": "Train",
  "parent": "home",
  "order": 10,
  "vars": { "tab": "active" },
  "rows": [ ... ],
  "softKeys": [ ... ]
}
```

| Key | Meaning |
|---|---|
| `id` | Unique page ID. `home` is the start page. Other pages link to this ID. |
| `title` | Shown in the header and on the menu button. |
| `parent` | Page this one belongs to. Default `home`. A parent with a `menu` row lists it as a button; any other parent gets a soft key for it, which makes it a subpage (Locomotive is one of Train). The header shows the parent's title before the page's. The home page lists no pages; the Start menu does. |
| `hidden` | `true` keeps the page out of every menu and parent; it opens only through links. Header taps open pages too: the clock opens `stopwatch`, the weather left of it opens `weather`. |
| `order` | Position in the parent menu, lowest first. Default 100. |
| `openLabel` | For pages with subpages: the label of the page's own entry in its Start menu list, above the subpages. Default the title. Station uses `"Station Jobs"`. |
| `parentKey` | `false` gives the page no soft key on its parent, e.g. when the parent has more subpages than key slots. The Start menu still lists it. Sim ports uses it. |
| `startMenu` | `false` keeps the page out of the Start menu, e.g. one its parent or other pages open with a soft key. Devices uses it, so Media Player opens straight from the Start menu. A page of nothing but `menu` rows, like Tools, is never listed itself: the Start menu shows its subpages. |
| `remote` | `true` for pages that show remote data (weather, jobs, station, fees, owned vehicles). Each visit starts with a loading bar before the page appears: 1-3 s at 3 radio bars, 3-5 s at 2 bars, 5-10 s at 1 bar, blending smoothly with the signal strength. The bar follows the signal live and stalls with no bars. |
| `vars` | Page variables with start values, read as `page.<name>`, changed by soft keys and buttons. The tablet keeps their current values through power cycles and saves them with the game. |

The content area is 780 x 406 px. Rows are stacked from the top; what doesn't fit is cut off.
A normal row is 26 px high.

## Rows

Every row can have `"when": "<condition>"`; it is only shown while the condition holds.

| Type | Keys | Shows |
|---|---|---|
| `text` | `text`, `style` | One line of text. |
| `heading` | `text` | Bold section title. |
| `divider` | | Thin line. |
| `field` | `label`, `value`, `labelWidth`, `style`, `lines` | Label and value. The value may contain links: `[[station:{route.nextStationId}]]`. With `lines` (default 1) the row is that many lines tall and a long value wraps onto them. |
| `pairs` | `items`, `labelWidth` | Several label/value pairs side by side: `[["Speed", "{train.speedKmh:F0} km/h"], ...]`. |
| `bar` | `label`, `value`, `min`, `max`, `text`, `warnWhen`, `labelWidth` | A bar filled from `min` (default 0) to `max` (default 100). `value`, `min` and `max` are paths, numbers or arithmetic without braces: `"value": "loco.fuel.pct"`. `text` is shown after the bar. The bar turns red while `warnWhen` holds. |
| `buttons` | `buttons`, `label` | A row of buttons, with an optional `label` in front with the same keys as a soft key (`label`, `link`, `set`, `when`, `enabledWhen`) plus `selectedWhen`: while it holds, the button is drawn pressed. An item without `link`, `set` or `planJobs` is shown as text, e.g. the value between - and + buttons. |
| `checklist` | `source`, `text`, `value`, `var`, `enabledWhen`, `perRow`, `empty` | A tick box for every item of a list. `text` and `value` are filled per item (`{item.id}`). The page variable `var` keeps the values of the unticked boxes, comma-separated, so all boxes are ticked while it is empty. A box whose item fails `enabledWhen` is unticked and greyed out. |
| `list` | `source`, `columns`, `rows`, `rowHeight`, `empty`, `filter`, `sort`, `descending`, `limit`, `var`, `value`, `click` | A list from a binding, `rows` lines per screen; without `rows` it takes the height the other rows leave and fits as many lines as it can. Longer lists get `Page -` / `Page +` keys in the rightmost slot. With `var`, a click on a row selects it or clears it again: the page variable `var` keeps the `value` of every selected row (default `{item.id}`), comma-separated, and selected rows are drawn pressed. With `click` instead, a tap on a row does what that key does (`set`, `link`, `planJobs`, as on a soft key), with the row's `item` in its templates. |
| `menu` | | Buttons for every page whose `parent` is this page. |
| `widget` | `name`, `rows` | A built-in part with its own logic: `timetable` (EbuLa table, `rows` lines) or `planner` (route planner; with the Passenger Jobs mod it also offers its rural stations; with the 1500 V DC Catenary mod its "Wire only" button keeps the route under the overhead wire, apart from the track the train stands on, and offers only stations with a track under it) or `plannerStops` (the planner's stops on the way, driven in list order) or `jobs` (job tabs with the Active toggle) or `stopwatch` (Start/Stop and Reset keys; keeps running on other pages until the tablet is switched off) or `tracks` (the nearest station's tracks as bars by sub-yard, with the cars on them and the free length; a tap on a track lists its cars with their cargo and jobs, a tap on a car opens its job) or `job` (one job in detail: route, cars, pay, licenses, tasks and each car's next task; `var` names the page variable that holds the job ID) or `forecast` (the game's forecast for the day in slots of 3 hours, with the icons of the forecast posters at the stations and, with DVSeasons, an estimated temperature for each) or `mediaPlayer` (the music files (.mp3, .ogg, .wav, and through the bundled `ffmpeg/ffmpeg.exe` also .m4a, .aac, .flac, .wma, .opus, .ape, .mka, .webm) in the music folder set in the Unity Mod Manager settings, by default the Windows Music folder, browsed by folder: a tap opens a folder, the `../` row at the top of the list or Back leaves it, a tap on a song plays it and the songs after it in that folder, Play all plays the folder from the top; above the list the song playing and square Stop, Backwards, Play, Forwards, Pause and Shuffle keys; at the bottom a volume bar with - and + keys, kept as `settings.mediaVolume`, yellow from 60 % and red from 90 %; a Rescan soft key; a Spectrum soft key that shows a spectrum of what plays instead of the folder and its songs, kept as `settings.spectrum` for all three player pages (List goes back); music goes on on other pages until the tablet is switched off, and the header shows a play or pause symbol that opens the page) or `mediaDevices` (the bought speakers and the subwoofer the tablet plays through, each with its own volume, an EQ button that opens Lows, Mids and Highs, -12 to +12 dB, for the subwoofer only Lows (Back leaves the EQ), and an IDENT button that blinks the device's LED blue for 10 seconds, seen only by the player who pressed it) or `radio` (internet radio: the station and its song, Stop / previous / Play / next station keys, and tabs for the game's and your own stations from `radio/stations.json`, and for the genres and countries of the radio-browser.info directory, whose stations can be saved to your own; from two signal bars up it plays clean, at one bar the slow connection stops now and then to buffer, with no bars it buffers until the signal returns; plays through the media player, its speakers and volume; the Spectrum soft key shows the spectrum instead of the stations) or `pcAudio` (PC Audio, Windows only: a Windows program such as Spotify or a browser played through the tablet with the bundled `capture/ratcapture.exe`; the song with "Capturing: <program>" below it, Stop / Previous / Play / Pause / Next keys where Windows' media controls know the program, a Programs soft key that opens the programs playing sound now to pick from (Back or a pick leaves it), a Spectrum soft key that shows the spectrum in the free space, and the media player's volume; the program's sound is boosted by the PC Audio boost from the Unity Mod Manager settings, 0 to 24 dB, by default 0, as it comes after the program's own volume and can play far quieter than a local song otherwise, with peaks rounded off softly; the program is silent on the PC while the tablet plays or pauses it and gets its volume back on Stop, when other media plays, on switching the tablet off or quitting; a double-click on `capture/ratcapture.exe` puts every program's volume back after a crash; the program played last is kept as `settings.pcAudioApp`; under Proton or Wine the bundled `capture/ratcapture-linux` plays a Linux program instead, through PipeWire or PulseAudio and MPRIS, with the same boost and `./ratcapture-linux cleanup` as the failsafe). |

List columns: `header` (caption in a header row above the list), `text` (may use `{item.*}`), `width` in px or `flex` (share of the remaining width),
`align` (`left`, `right`, `center`, `top`), `style`, `when`, `warnWhen` (the cell turns red while it holds,
e.g. `"item.missingLicenses"`).

List items can be picked and ordered before they are shown:

| Key | Meaning |
|---|---|
| `filter` | Condition every shown item meets, e.g. `"item.coupled && item.pay > 1000"`. |
| `sort` | Path or arithmetic to sort by, e.g. `"item.pay"`. Numbers sort as numbers, everything else as text. May be a placeholder such as `"{page.sort}"`, so buttons can change it. |
| `descending` | `true` to sort from high to low. May also be a placeholder (`"{page.desc}"`) or a condition. |
| `limit` | Show at most this many items. |

Sort buttons, as on the Station page: the page keeps the order in its `vars`, and the buttons set them.

```json
"vars": { "sort": "item.pay", "desc": "true" },
...
{ "type": "buttons", "buttons": [
    { "label": "Pay", "set": { "sort": "item.pay", "desc": "true" }, "selectedWhen": "page.sort=item.pay" },
    { "label": "Type", "set": { "sort": "item.type", "desc": "false" }, "selectedWhen": "page.sort=item.type" }
  ] },
{ "type": "list", "source": "station.jobs", "sort": "{page.sort}", "descending": "{page.desc}", "columns": [ ... ] }
```

```json
{ "type": "list", "source": "jobs.cars", "filter": "item.coupled && item.action=unload", "sort": "item.to",
  "columns": [ { "text": "{item.id}", "width": 120 }, { "text": "{item.to}", "flex": 1 } ] }
```

Styles: `normal`, `dim`, `highlight`, `warning`, `bold`, `large`.

## Soft keys

The bottom row starts with the square Start key showing the R.A.T. logo; a page gets the **seven** slots right of it.
Start opens a list of the home menu's pages over the page, every page with subpages opening a further list, and Desktop for the home page. There is no Back key: while the header shows `<` left of
the title, tapping either goes back. Widgets add their own keys after the page's keys. A list that needs paging adds
`Page -` and `Page +`, which share one slot and always take the rightmost one. Keys that don't fit into the seven slots
are dropped and reported in the log.

```json
{ "label": "Station info", "link": "station:{route.nextStationId}", "when": "route.hasNextStation" }
{ "label": "Coupled", "set": { "tab": "coupled" }, "enabledWhen": "page.tab!=coupled" }
```

| Key | Meaning |
|---|---|
| `label` | Key caption. |
| `link` | Page to open. Disabled when no such page exists. `set` and `planJobs` run first. |
| `linkVars` | Variables of the linked page to change before it opens: `{ "jobsTab": "Plan" }` opens the Jobs page on its Plan tab (tabs: `Accepted`, `Coupled`, `Active`, `Plan`). |
| `planJobs` | Job IDs, comma-separated, to mark active for the shunting planner on the Jobs page, e.g. `"{page.selected}"`. Jobs that are not taken yet show there as "not taken" and are planned like taken ones. Runs before `set`. |
| `set` | Page variables to change; the page is rebuilt. Names starting with `settings.` change a tablet setting instead: `{ "settings.showSignalAspects": "false" }`. Values are templates, so a key can count up: `{ "settings.brightness": "{settings.brightness + 10}" }`. |
| `right` | `true` puts the key at the right end of the bar. A list's `Page -` / `Page +` still come after it. |
| `when` | Key is left out unless the condition holds. |
| `enabledWhen` | Key is shown but greyed out unless the condition holds. |

## Placeholders and conditions

`{path}` inserts a value, `{path:F0}` formats a number (.NET format strings: `F0`, `F1`, `0.0`, `N0`).
A format can have sections for positive, negative and zero values: `{item.payPerMin:$0/min;-0;--}` shows `--` for 0.
An unknown path shows as `{?path}` on screen.

Placeholders can calculate with `+ - * / %` and brackets: `{train.lengthM / 18:F0} cars`,
`{(loco.fuel.capacity - loco.fuel.amount):F0} to fill`. Division by zero shows `--`.

Conditions: `path` (true when not false, empty, 0 or an empty list), `!path`, `path=value`, `path!=value`,
`a in b` and `a !in b` (true when a value of `a` is, or isn't, one of `b`; either side may be a list,
text on the right is split at commas: `item.typeShort !in page.hideTypes`, `page.tab in jobs,filter`),
and number comparisons `<`, `<=`, `>`, `>=`, whose sides may calculate: `loco.fuel.pct < 20`,
`train.lengthM > station.tracks.count * 10`. Join parts with `&&` (all must hold) and `||` (one must hold);
`&&` binds tighter, so `a && b || c` means "a and b, or c". There are no brackets in conditions.

## Bindings

**train**: `loaded`, `onTrain`, `derailed` (a car of the train is off the rails), `moving`, `number` (the coupled job the train runs as, an active one first;
`+2` when two more jobs are coupled), `speedKmh`, `massT`, `lengthM`, `wagonCount`,
`brakeDistance`, `emergencyDistance` (text), `decelerationMs2`, `maxDecelerationMs2` (estimates in m/s²),
`brakeEffectivenessPct` (lowered by wagon damage), `altitudeM` (above sea level), `gradePercent` (slope under the
tablet's car in its direction of travel), `wagons` (list), `cars` and `groups` (lists, see below).
Each wagon: `id`, `type` (livery ID, e.g. `FlatbedStakes`), `name` (its name in the game's language), `massT` (car + cargo), `cargoMassT`, `lengthM`, `cargo` (name), `state`, `derailed`, `damaged`.

`cars` lists every car, locos too, from the end with a loco. `groups` puts neighbouring cars of one job that go to
the same track next into one row, like the dv-hud mod. Both have the same fields, so one list layout shows either:
`pos` (`3` or `3-7`), `count`, `id` (first car), `type` (livery ID, empty when they differ), `name` (`4x Flatbed`, or `4 cars` when they differ), `isLoco`, `massT`,
`cargo` (`mixed` when they differ), `state`, `hasJob`, `job`, `jobTaken`, `destination` (next track of the job),
`atDestination`, `derailPct` (highest build-up towards derailing, 100 derails), `hasCoupler`, `couplerKn` (highest
force in the couplings, each to the car before), `pipeBar`, `reservoirBar` (lowest; for a loco its main reservoir),
`cylinderBar`, `controlBar` (control reservoir, lowest), `brakePct` (highest), `brakeTempC` (highest; the brakes lose
force from 600), `handbrakes` (count), `handbrake` (text),
`valve` (with the Realistic Brakes mod: `G`/`P` goods or passenger European valve, `AB` American valve, `cut out`
when isolated; `mixed` in a group of different ones; empty without the mod). `train.realisticBrakes` is true while that mod runs the brakes.

**loco**: `present`, `type` (livery ID, for links like `car:{loco.type}`), `name`, `throttle`, `reverser`, `trainBrake`, `independentBrake`,
`handbrake` (text), `brakePipeBar`, `mainReservoirBar`, `brakeCylinderBar` (bar above atmosphere, 0 when empty).
With the Realistic Brakes mod: `hasDriverValve`, `driverValve` (`cut in`, or `cut out` on a trailing engine),
`equalizing` (equalizing reservoir, text, `--` without one).
Engine (diesel only): `hasEngine`, `engineOn`, `engine` (text), `rpm`; `hasTemperature`, `temperatureC`.
Fee: `hasFee` (false for a loco nobody charges for, e.g. one spawned in sandbox), `feeUsd`, `fee` (text, tax included,
as the career manager lists it; electricity and other modded resources are part of it), `feeIsService` (the player's
own loco, paid when it is serviced).
Odometer: `hasOdometer` (false on a multiplayer client until the host sent it), `odometerKm` (distance the loco has
run in this savegame, counted for every loco whether a tablet is aboard or not).
Traction motors (diesel-electric and electric): `hasTmTemperature`, `tmTemperatureC`, `tmTemperature` (text),
`tmOverheatC` (above it the motors overheat and can burn out; 120 when the loco does not say).
Steam: `hasBoiler`, `boilerBar`, `boilerWaterPct`, `hasFirebox`, `fireOn`, `fireboxC`, `hasCutoff`, `cutoffPct`,
`cutoff` (text, `--` without one).
Traction: `hasTraction`, `tractiveEffortKn`, `powerKw`, `adhesionKn` (most force before the wheels slip),
`slipPct`, `slidePct` (braked wheels sliding).
Resources `fuel`, `oil`, `sand`, `water`, `coal`, `charge`, each with `has`, `pct`, `amount`, `capacity`,
e.g. `{loco.fuel.pct:F0} %` with `"when": "loco.fuel.has"`. Use per minute, estimated from the change since the
last refresh: `draining` (false while the level stays or goes up), `perMin` (in the units of `amount`), `pctPerMin`
(percent of `capacity`), `timeLeft` (text, `--` while not draining), e.g. `{loco.fuel.perMin:F1} /min` with
`"when": "loco.fuel.draining"`.
`ports` (list of every simulation value: `id`, `type`, `value`) and `port`, which reads one of them by its ID:
`{loco.port.engine.RPM:F0}`. The IDs differ between loco types; the **Sim ports** page in the Info menu lists
the ones of the loco you are in.

**solar** (from the Solar Gadgets mod; power in W, energy in Wh of game time, rates per game hour):
`installed`, `present` (a car of the train has solar panels, batteries or a simulated loco battery),
`cars`, `batteries`, `consumers` (lists). Each car is a network of its own: no cable links two cars, so there is no
total for the train.
Each car: `id`, `hasBattery`, `chargePct`, `charge` (text), `capacityWh`, `capacity` (text, e.g. `840 Wh`, `28.8 kWh`), `supplyW`, `supply` (text, e.g. `12.5 W`, `1.2 kW`),
`demandW`, `demand` (text), `ratePctH` (negative
while draining), `rate` (text, e.g. `-4.2 %/h, empty in 11 h 5 min`, `Full`, `Empty`, `Steady`, `No battery`, or
`Not charging` / `Not draining` when no battery takes part, like a loco battery while the loco's electrics are off).
Each battery: `car`, `name`, `soldered`, `chargePct`, `charge` (text), `capacityWh`, `capacity` (text). Each consumer: `car`, `name`,
`drawW`, `draw` (text).

**electric** (the shown loco's draw from the overhead wire of the 1500 V DC Catenary mod; locos built with
Catenary for CCL, such as the WE-4U, and the 2WE3-981, whose unit A is read from either unit):
`present`, `source` (`Catenary for CCL` or `2WE3`), `car` (the car read), `voltageV`, `voltage` (text),
`hasNominal`, `nominal` (text, the voltage the loco is built for), `currentA` (negative while feeding power back),
`current` (text), `powerKw`, `power` (text), `regenerating`,
`pantographsReported` (the 2WE3 lists the roof pantograph and the sidepan of each unit; there, contact means a live
wire, because the mod knows no other contact), `raisedCount`, `offWire` (a pantograph is up, but none touches a wire),
`pantographs` (list), `hasMotorTemperature`, `motorTemperatureC`, `motorTemperature` (text), `hasWorkingMotors`,
`workingMotors`, `hasEnergy` (the loco has an electricity meter), `energyKwh`, `energy` (text, `--` when the meter's
kWh factor could not be read): what the meter counted since the loco's fees were last paid. What it costs is part of
`loco.fee`.
Each pantograph: `name` (its component ID), `raisedPct`, `raised` (text), `contact`, `wire` (text, the wire's voltage
or `no contact`).
The WE-4U's traction battery: `hasBattery`, `onBattery` (the battery feeds the motors instead of the line),
`batteryWanted` (its switch is on), `batteryVoltageV`, `batteryVoltage` (text), `batteryCurrentA`, `batteryCurrent`
(text), `batteryPowerKw`, `batteryPower` (text), `batteryLevelPct`, `batteryLevel` (text).

**damage** (the shown loco's damage, as the game's damage controller keeps it): `present`, `parts` (list),
`blownCount`, `fuses` (list), `hasMotors`,
`workingMotors`, `totalMotors`, `motors` (text, e.g. `4 of 6`), `motorsLost`.
Each part: `name` (`Body`, `Wheels`, `Mechanical powertrain`, `Electrical powertrain`; a loco without one leaves it
out), `healthPct`, `health` (text).
Each fuse of the simulation: `id`, `on`, `state` (`on` or `off`).

**firing** (the shown loco's fire, boiler and engine from the Realistic Firing mod, as its F6 overlay shows them;
units: `Pct` percent, `Bar` gauge bar, `C` °C, `Kg`, `KgPerS`, `S` seconds, `Ppm`): `present` (false unless the mod
fires the loco), `fuel` (`coal` or `oil`), `coal`, `oil`. Every key below is there with a default when the mod or a
part is missing; each section's `present` tells. A level is `ok`, `watch` (wants doing soon) or `act` (wants doing
now), with flags of the same name plus `Watch` and `Act`, e.g. `firing.fire.levelAct`.
`fire`: `lit`, `state` (`lit` or `out`), `heatPct` (good 40 to 90; far past 100 on a cold start), `heatBarPct` (0 to
100), `level`, `draftPct` (air against what the fire wants, 100 right), `draftBarPct` (0 to 250), `draftLevel`,
`smokePct` (0 clear to 100 black), `hasTemperature`, `temperatureC` (coal only).
`advice` (the next step at the foot of the overlay): `present`, `text`, `level`.
`grate` (coal): `present`, `coalKg`, `ashpanPct`, `ashpanFull`, `clinkerKg`, `needsCleaning`, `hasDoor`, `doorPct`,
`hasThinZone`, `thinZone` (e.g. `front left`), `zoneReferenceKg` (a zone's working bed), `zones` (list of nine, from
the fire door to the tubes, each row left to right: `name`, `coalKg`, `bedPct` (against the working bed),
`burningPct`, `clinkerKg`, `thin`), `rows` (the same as three rows `front`, `middle`, `back`, so the tubes are on top as seen from the cab: `row`, `left`,
`center`, `right` (text, e.g. `21 kg, 48 %`), `leftThin`, `centerThin`, `rightThin`).
`coalState` (coal): `present`, `moisturePct`, `condition` (`dry`, `damp`, `wet`), `wet`, `hasCover`, `cover` (`open`
or `shut`). `stoker`: `present`, `jammed`, `speedPct`, `tableKg`, `tableLoaded` (wants raking), `jetsBar`.
`burner` (oil): `present`, `firingValvePct`, `hasStop`, `stopPct`, `feedKgPerS`, `atomizerPct`, `atomizingPct`
(under 100 too little steam for the oil), `oilC`, `heaterPct`, `brickC`, `onShedSteam`, `steamLinePct`,
`flueSootPct`, `lighting` (true while the fire is out), `steps` (list, the steps to light it in order: `number`,
`title`, `how`, `done`, `current`, `mark` (`[x]`, `>` or `[ ]`)).
`boiler`: `present`, `pressureBar`, `workingBar` (where the safety valves lift), `pressurePct` (good 85 to 100),
`pressureBarPct`, `pressureLevel`, `glassPct` (the gauge glass, good 40 to 75), `glassBarPct`, `glassLevel`,
`hasWaterTemperature`, `waterC`, `heating` (raising steam from cold), `hasFouling`, `sootLossPct`, `scaleLossPct`,
`hasImpurities` (the mod's impurities setting is on), `impuritiesPpm`, `foamPct`, `hasPlug`, `plugMelted`,
`plugCoreC`, `hasAlarm`, `alarmSounding`.
`hasInjectors`, `injectors` (list of the mod's injectors; empty when the engine's own work): `name`, `state` (`off`,
`priming`, `working`, `hot`, `low pressure`, `no water`, `broken`), `fault`, `waterKgPerS`, `tenderShut`, `tender`
(`tender shut` or empty), `setting` (text, the steam and water valves or the lever).
`overflow`: `present`, `waterKgPerS`, `steamKgPerS` (wasted). `feedPump`: `present`, `state` (`off`, `feeding`,
`choked`, `racing`, `lost suction`, `tripped`), `fault`, `waterKgPerS`, `temperatureC`.
`engine` (while the mod works the cylinders): `present`, `cutoffPct`, `steamChestBar`, `meanPressureBar`,
`steamKgPerS`, `condensingPct`, `superheated`, `superheatC`, `steamC`. `booster`: `present`, `state` (`cut out`,
`idling`, `cocks open`, `working`), `warmPct`. `fireman` (the mod's automatic fireman): `present`, `mode` (`off`,
`keep alive`, `shunting`, `road`), `hasCall`, `call` (`heavy ahead`, `easing off`), `callLeftS`.

**integrations** (other mods whose data the tablet reads): `anyFailed`, `list`. Each one: `name`, `state` (`Active`
while the mod is loaded and read, `Inactive` while it is not loaded, `Failed` when it is loaded but could not be read;
the game log says why), `active`, `failed`.

**route**: `loaded`, `onTrack`, `track`, `direction`, `limitKmh` (0 when unknown), `limit` (text, `--` when unknown; with the `showTrainClear` setting on, the lowest limit anywhere under the train),
`trackLimitKmh`, `trackLimit` (the limit at the tablet's car, whatever the setting), `grade` (text),
`trackDamaged`, `trackDamagePct`, `trackClosed` (with the Track Maintenance mod: the car is on track that mod has damaged
enough to advise a lower speed, how much, and whether it is closed; see below),
`hasPlan`, `destination` (of the planner's route, `--` without one; the scan itself always follows the switches), `hasNextStation`, `nextStationId`, `nextStation`, `nextStationDistance` (text),
`next` (name and distance of the next station, `--` when none is in the scanned range),
`events` (list), `scannedM` (how far ahead the scan reached), `speedSections`, `gradeSections` (lists),
`delay` (text: `+3 min` late, `-2 min` early, `on time`, `--` when unknown), `delayS`, `late`, `hasDelay`. The delay
compares the time to the train job's destination, driving at the speed limits (40 km/h where none is known), with the
job's time left; it is known when the job has a time limit and its destination is within the scanned route.
Each event: `distance` (text), `distanceM`, `kind` (`Station`, `Track` (entering a station track), `Switch`, `Signal`, `Speed`, `Grade`, `WireBegin` and `WireEnd` (the 1500 V DC Catenary mod's wire, overhead or side rail, begins or ends; gaps up to 60 m do not end it; its `name` ends in the system voltage of the wire ahead, e.g. `Overhead wire begins, 1500 V`, as does an `End of neutral section`), `Neutral` and `NeutralEnd` (a neutral section of that mod begins or ends), `Damage` and `DamageEnd` (track the Track Maintenance mod has damaged begins or ends), ...), `name`, `stationId`,
`warning` (end of track), `arrival` (estimated game time at stations, `HH:mm`), `track` (at stations: the station
track the route leads onto as the switches are set, e.g. `HB-D7L`), `aspect` and `restrictive` (signals only),
`damagePct` and `locked` (switches only, with the Junction Maintenance mod: its damage, 0 without the mod, and
whether it cannot be thrown by hand), `trailing` (the route runs through the switch from one of its branches; such
a switch is listed only while that mod has it damaged or locked). The `timetable` widget writes damage and lock
behind the switch and shows it in red.

With the [Track Maintenance](https://github.com/gercj187/TrackMaintenance) mod, track it has damaged counts as a speed
restriction: that mod advises 60 km/h from 5 % damage of a section, 10 km/h less for every 5 % more, and closes the
track from 35 %. The game enforces none of it. Damaged sections less than 50 m apart are one restriction with the
lowest of their limits. Inside one, `limit`, `speedSections`, the arrival times and `delay` use the lower of it and
the sign; a closed stretch shows as `0 km/h` and counts like a stretch without a limit for the times. The
`timetable` widget lists each restriction with its limit and damage in red, and where it ends.

Signals come from the [DV Signals](https://github.com/WhistleWiz/dv-signals) mod when it is installed; `aspect` is
the name the signal pack gives the aspect, empty while the signal is off, and `restrictive` is true when it
doesn't allow passing.
Each section: `start`, `end` (text), `startM`, `endM`, `lengthM`, and `limitKmh` (speed) or `percent` (grade, positive uphill).

**jobs**: `loaded`, `acceptedCount`, `coupledCount`, `activeCount`, `cargoT`, `accepted`, `coupled`, `active` (lists; active = marked on the tablet),
`cars` (every car of every accepted job, one list).
Each job: `id`, `type`, `typeShort` (`FH`, `LH`, `SL`, `SU`; Passenger Jobs: `PE` express, `PR` regional), `licenses` (text, e.g. `HZ2 TL1`: only the highest tier of each family, without the job type's own FH/LH/SH; Passenger Jobs' are `PAX1` and `PAX2`),
`licenseIds` (list), `missingLicenses` (list of the required licenses you don't have, the job type's one included), `from`, `to`, `fromId`, `toId`, `fromTrack`, `toTrack` (first and last remaining transport), `cargo`, `cargoT`,
`wagons`, `lengthM`, `massT`, `pay` (without bonus), `bonus` (for finishing in time; 0 when untimed), `payWithBonus`,
`payPerMin` (pay with bonus per minute of the time limit; 0 when untimed), `timed`, `timeLimit`, `timeLeft` (text, negative when late), `timeLeftS`, `late`, `coupled`, `active`,
`steps` (remaining tasks, one per line).
Each job car: `jobId`, `jobActive`, `id`, `type`, `name`, `track` (where it stands), `lengthM`, `coupled`, `action` (`move`, `load`, `unload`
or `done`), `from`, `to` (tracks of its next task), `legs` (all remaining tasks, one per line).

**world**: `loaded`, `hasTime`, `time` (game time, `HH:mm`), `realTime`, `date`, `hour` (14.5 = half past two), `day`, `raining`, `rainPct`,
`wetnessPct`, `money`, `licenses`, `fees`, `owned` (lists), `feesTotal`.
Each license: `name`, `kind` (`job` or `general`).
Each fee: `id` (the job or car it is for), `kind` (`job`, `loco`, `owned` or `other`), `price`, `payable` (false while it can't be paid yet).
Each owned vehicle: `id`, `type`, `name`, `servicePrice`, `destroyed`, `track`, `distance` (text), `distanceM`.

**weather** (the game's daily forecast, made at 06:00 for the next 24 hours, as on the posters at the stations):
`available`, `date` (of the forecast, `yyyy-MM-dd`), `icon` (the game's icon type for this hour, e.g. `LightRain_Day`),
`now` (it in words, e.g. `Light rain`), `forecast` (list).
Each slot: `from`, `until` (`HH:mm`), `icon`, `text`, `now`, `past`, `cloudPct`, `rainPct`, `fogPct`, `hasTemp`, `temp`
(estimated with DVSeasons' formula for the slot's weather; blizzards left out).

**seasons** (from the [DVSeasons](https://github.com/HerodrineYT/DVSeasons) mod; the base game has no air temperature):
`installed`, `ready` (DVSeasons knows the temperature; false while the world loads), `season`, `next` (`Spring`,
`Summer`, `Autumn`, `Winter`), `changing`, `transitionPct` (how far the turn to `next` is), `snowPct`, `tempC`, `temp`
(text, e.g. `-4 °C`; a blizzard included), `blizzard` (DVSeasons' own line, e.g. `No blizzard forecast`).

**station** (the one picked with the `station` setting, else the one nearest to the player): `known`, `id`, `name`, `distance` (text), `distanceM`, `here` (inside the
area where it offers jobs), `picked` (picked rather than the nearest), `all` (every station by name: `id`, `name`, `distance`, `distanceM`),
`jobsKnown` (the offers are known: while `here`, or anywhere with the Persistent Jobs mod, which keeps them),
`jobs` (list of offered jobs, while `jobsKnown`; same fields as a job above), `tracks` (list), and the filter options of the offered jobs: `jobTypes` (`id`, `name`), `destinations` (`id`, `name`).
Each track: `id`, `lengthM`, `freeM`, `usedPct`. The `tracks` widget draws them with their cars. With the Persistent Jobs mod, the cars it has taken out of the world at far-away stations are drawn too.
With the [Junction Maintenance](https://github.com/gercj187/JunctionMaintenance) mod: `switchesKnown` (the mod is loaded and read), `damagedSwitches` (count),
`switches` (list of the station's switches, the ones that mod's career manager lists there, most damaged first).
Each switch: `id` (e.g. `S-0228-HB`), `damagePct` (whole percent, 1 at least while any damage is left), `damaged`,
`repairing`, `locked` (it cannot be thrown by hand: under repair, or at full damage while that mod locks such switches),
`state` (`under repair`, `locked` or empty).
With the Track Maintenance mod: `trackDamageKnown` (the mod is loaded and read), `trackDamage` (list of the damaged
sections of the station's tracks and of the tracks through its switches, most damaged first; sections on the open
line are on the Route timetable only).
Each section: `track` (its ID, e.g. `HB-D7L`; for a track through a switch the switch's ID, as that mod lists it),
`fromM`, `toM` (from the track's start), `range` (text, e.g. `40-60 m`), `damagePct`, `hasLimit`, `limitKmh`,
`closed`, `limit` (text: `30 km/h`, `closed`, or `--` below 5 % damage).

**page**: the page's `vars`.

**settings**: the tablet's settings from the Settings page (in the Start menu);
saved with the game. `showSignalAspects` (`true`/`false`): the Route timetable shows what each signal shows.
`showTrainClear` (`true`/`false`): the Route timetable adds a ¥ row wherever the limit for the whole train rises,
i.e. where its end leaves the last lower section (also ones the front has already passed), with the limit that then applies.
`markMisalignedSwitches` (`true`/`false`): while a route is planned, the Route timetable shows the switches ahead
that are set against it in red.
`showSwitchDamage` (`true`/`false`): with the Junction Maintenance mod, the Route page shows the damage of the
switches ahead and lists damaged ones the route runs through from a branch.
`showTrackDamage` (`true`/`false`): with the Track Maintenance mod, the Route page counts damaged track ahead as a
speed restriction. Off, limits, times and timetable are as the signs give them.
`rotation` (`standard`/`inverted`): `inverted` turns the screen upside down, for a tablet mounted to the roof.
`theme` (`light`, `dark`, `hotdog`, `colorful`, `pastel`): the colors of the screen. Unknown names show `light`.
`buttons` (`simple`, `win2000`, `round`, `outline`, `pill`): the shape of the buttons. Unknown names show `simple`.
`brightness` (percent, `10` to `100`): how bright the screen is.
`station` (a yard ID, empty for the nearest station): the station the Station and Yard pages show; picked on the Station page.

Any list also has `count`, e.g. `{train.wagons.count}`. So does a page variable, counting its comma-separated values:
`page.selected.count=1` holds when exactly one row of a selectable list is picked.

## Page IDs to link to

Pages in this folder by their `id`, plus generated ones: `info` (the Reference menu), `stations`, `cargo`,
`rollingstock`, `station:<yard ID>` (e.g. `station:HB`), `cargo:<cargo ID>`, `car:<car type or livery ID>`.
