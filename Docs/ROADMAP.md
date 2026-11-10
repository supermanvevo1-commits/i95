# Survive the I-95: production roadmap

One milestone at a time. Each step is tested live in the editor, then committed. Before a step is checked off, it gets a second look: does it behave like real driving and real drivers on that road? Does it still make sense next to the steps before it? If not, it's reworked, and earlier steps are reopened when needed. A milestone closes only after the creator plays it and every "Done when" item passes. New ideas go in the Ideas list, not the current milestone.

## M0 Foundation (done)
- [x] Unity URP project and Git
- [x] Data files for cars, traffic and routes
- [x] Free asset pipeline with license credits
- [x] Real road import from OpenStreetMap
- [x] Live editor test bridge

## M1 The car (done)
- [x] Car sits on its tires
- [x] Front wheels steer, all wheels spin
- [x] Tire-grip physics with no driver guard
- [x] Chase and cockpit cameras, mouse look
- [x] Working gauges and mirrors
- [x] Gear lever follows a staggered P, R, N, D gate
- [x] Creator confirms steering feel and no wheel stutter
- [x] Commit

Done when: creator approves the feel, and the car can be driven and lost.

## M2 Crashes (done)
- [x] 2.1 Solid barriers, overpass piers, signs and trees
- [x] 2.2 Impact response: bounce, spin, speed loss by angle
- [x] 2.3 Damage value and what it affects (top speed, steering pull)
- [x] 2.4 Visible damage: smoke, loose parts, dents
- [x] 2.5 Crash camera shake and impact effects
- [x] 2.6 Reset or recover after a wreck

Done when: hits at 30, 70 and 120 mph all feel different, and damage changes how the car drives.

## M2b Miami test road (done)
Move testing to where the game starts, so traffic and police are tuned in the real setting.
- [x] Import Downtown Miami to Fort Lauderdale as the test road
- [x] Elevated highway sections and bridges with height
- [x] 12 drivable exits with one engineered shape: speed-change lanes, clean splits, wide curves and merges (road network that traffic will also use)
- [x] Exit cues: road arrows, striped split areas, advance, split, merge, ramp speed and speed limit signs
- [x] First-pass skyline and buildings from real map footprints
- [x] Sound walls and urban roadside

Done when: it reads as Miami from the driver's seat and holds its frame rate.

## M3 Traffic
- [x] 3.1 Spawn and remove cars around the player
- [x] 3.2 Lane keeping and following distance
- [x] 3.3 Placeholder models from the vehicle catalog
- [x] 3.4 Lane changes with signals
- [x] 3.5 Trait system (attention, aggression, lane discipline)
- [x] 3.6 First drivers: normal, left-lane camper, tailgater, trucks
- [x] 3.7 Collisions between traffic and the player
- [x] 3.8 Distant-traffic simplification and a 40-car performance check

Done when: 40 cars run smoothly, no crashes happen without a cause, and a camper is recognizable.

## M3b Road Life (current)
Traffic with memory, real road rules, and places where it piles up. Comes before police, so police have scenes, speeders and road rage to react to. See VISION.md for the research behind each number.
- [x] 3b.1 Regulars: drivers you have a run-in with stay in the run with the same car, keep driving out of sight, and can catch up later
- [x] 3b.2 Grudges: cutting off, brake-checking, tailgating, holding up or hitting someone makes them angry, and they cool off slowly
- [x] 3b.3 Road rage that escalates: horn and high beams, riding your bumper, pulling alongside to yell, cutting in and brake-checking, ramming
- [x] 3b.4 Short on-screen lines when it matters ("The red pickup truck is back.")
- [x] 3b.4a Temperaments from real survey odds: most drivers shrug it off or honk, few follow, very few ram
- [x] 3b.4b From playtest notes: narrower field of view ([ and ] adjust it), right-click mirror glances, look back, mouse-wheel cruise control
- [x] 3b.4c GTA-style keyboard and mouse: V camera, C or back side button to look back, E horn, front side button to floor it
- [ ] 3b.5 Real speed limits per section, and drivers who mostly run 5-9 over, with a few far over in any lane
- [ ] 3b.6 Distracted drivers: about 3 in 100 on their phone, drifting, reacting late, causing a share of crashes
- [ ] 3b.7 Accidents between other drivers in real proportions (rear-end, sideswipe, run off the road, debris, rare freak events like a gator or a spill), blocking lanes, with rubbernecking in both directions
- [ ] 3b.8 Construction zones: lane closures, merges, lower limits, and cones and barrels you can hit and scatter
- [ ] 3b.9 95 Express lanes: pay-by-plate toll lanes that cost more as traffic builds. Pay to skip the jam, or cut through the poles
- [ ] 3b.10 Time of day and rush hour: morning and evening peaks near Miami and Fort Lauderdale with stop-and-go jams from volume alone, and cheaper distant cars so a jam can hold hundreds

Done when: a driver you wronged comes back and you recognize them, a jam forms and clears believably, and the frame rate holds in a jam.

## M3c Vehicle stats
Every vehicle drives like what it is. The same stats drive the player car and traffic. Comes before police, so pursuit balance is set against real cars.
- [ ] 3c.1 A stats sheet for every vehicle from real specifications: top speed, 0-60, 60-0 braking distance, cornering grip, weight, wheelbase, steering speed, handbrake, fuel tank and mileage
- [ ] 3c.1a Weighty feel in the spirit of GTA IV: soft suspension, body roll and dive, weight that shifts in corners and under braking, momentum you have to manage. Our own model, tuned per vehicle. No game's code or files are copied
- [ ] 3c.2 Variety inside a class: a family sedan and a sport sedan feel different
- [ ] 3c.3 Heavy vehicles: semis, box trucks, RVs and anything towing accelerate slowly, brake long, lean, and trailers sway
- [ ] 3c.4 Motorcycles: quick, light, lean into turns, and fragile in a crash
- [ ] 3c.5 Traffic accelerates, brakes and corners by the same stats
- [ ] 3c.6 Stat bars on a car card, ready for car select

Done when: a sedan, a pickup, a semi and a motorcycle each feel clearly different to drive.

## M3d Screen design
Every playtest looks at this, so it comes early.
- [ ] 3d.1 A designed HUD: speedometer, gear, cruise, damage, in one visual style with proper fonts
- [ ] 3d.2 Styled notices for run-ins and events, replacing plain text
- [ ] 3d.3 Minimap with the road ahead, exits, and later your destination and police
- [ ] 3d.4 Settings screen: field of view, controls, mouse and controller

Done when: the creator calls the screen polished.

## M4 Police and traffic law
Florida law first. Police are part of traffic, not only a chase.
- [ ] 4.1 Florida rules the game checks: speed tiers (100+ mph or 50+ over is criminal), Move Over, hands-free phone law, left lane, doubled fines in work zones with workers
- [ ] 4.2 Patrol cars in traffic, hidden in medians and behind overpasses. Everyone slows near them
- [ ] 4.3 Police pull over AI drivers too, with lights on the shoulder, and Move Over applies
- [ ] 4.4 Being noticed: a trooper sees you, a plate reader or aircraft spotter flags you and a unit is sent, other drivers call in what they see
- [ ] 4.5 Traffic stops: the officer walks up and talks, you pick answers, the officer runs your plate. Wait it out or run
- [ ] 4.6 Heat meter, tickets and their cost
- [ ] 4.7 Pursuit driving, PIT maneuver, roadblocks and spike strips, and bystanders who get caught in it
- [ ] 4.8 Busted and escaped outcomes
- [ ] 4.9 Per-state police profiles: Florida troopers in regular cruisers, Georgia State Patrol fast and quick to PIT

Done when: a chase can be won or lost, a stop can be talked through, and 5 minutes on the Miami test road shows police presence without a chase.

## M5 Run loop
- [ ] 5.1 Gas by real tank size and mileage, with prices by state
- [ ] 5.2 Bathroom, food and sleep
- [ ] 5.3 Money: start funds, gas, food, tickets, repairs
- [ ] 5.4 Gas stations and rest stops at real spacing, with pull-off ramps, signs, and exits that reach city streets
- [ ] 5.5 A passenger who can take the wheel while you sleep, driving by their own skill
- [ ] 5.6 Main menu, pause and car select
- [ ] 5.7 Save and load

Done when: one complete run works from menu to finish on the Miami test road, and running out of gas or money ends it.

## M6 Lives on the road
Every car is someone with somewhere to be. The story comes out of the systems, so every run is different.
- [ ] 6.1 Every AI driver has a life: who they are, where they're going, what they carry, and how they drive because of it
- [ ] 6.2 An event director that spaces out incidents and lets them feed each other: a crash, then a jam, then road rage in the jam
- [ ] 6.3 Mood and stress for your character: kids screaming, traffic, a crash. Stress changes how you drive and talk
- [ ] 6.4 The phone: first-person texting mini-game with choices and typing while you keep your eyes on the road, and the risk of being seen
- [ ] 6.5 Scenario: road trip with a partner
- [ ] 6.6 Scenario: family with kids and a crying baby
- [ ] 6.7 Scenario: trucker with cargo jobs, deadlines and trucker events
- [ ] 6.8 Scenario: smuggler, where preparation decides how a stop goes
- [ ] 6.9 Scenario: cannonballer, unlocked by money and upgrades (engine, tank, auxiliary fuel)
- [ ] 6.10 Free drive with no objective

Done when: three different scenarios on the Miami test road feel like three different games, and an AI driver's story visibly causes trouble for you.

## M7 Weather and time
- [ ] 7.1 Day and night, with a Miami night look: lit street lamps, lit building windows, neon
- [ ] 7.2 Headlights
- [ ] 7.3 Rain, wipers and windshield drops
- [ ] 7.4 Wet grip, hydroplaning and spray
- [ ] 7.5 NPC weather behavior: slowing, hazards on, pulling under overpasses, spinning out
- [ ] 7.6 Seasons and per-state weather odds (STATES.md): storms, lightning, fog, smoke, glare, flash floods
- [ ] 7.7 Rare big events, warned about on the radio first: tropical storm and hurricane evacuation, tornado
- [ ] 7.8 Snow and ice, for the northern states and winter runs: plows, black ice, cars in the median

Done when: a Florida downpour changes how you drive, and a hurricane evacuation feels like an event, not a filter.

## M8 Miami to Jacksonville
See FLORIDA_ROUTE.md for the map and section list.
- [ ] 8.1 Road generator handles interchanges, ramps and lane changes
- [ ] 8.2 Compression tool that shortens empty stretches, keeps landmark order
- [ ] 8.3 Bridges with height and water
- [ ] 8.4 Build sections in route order, one at a time, with real speed limits
- [ ] 8.5 Regional scenery: palms, swamp, coastal, pine
- [ ] 8.6 City skylines and roadside buildings from real map building data
- [ ] 8.7 Florida events: gators, downpours, Florida Man
- [ ] 8.8 Full NPC behavior list (npc_behaviors.md)
- [ ] 8.9 Art pass: decide the final look (semi-real or stylized), fictional car designs with no real badges, roadside variety, lighting, proper trees
- [ ] 8.10 Animated people in the car: the driver steers and shifts, a partner in the passenger seat, kids in the back, reacting to the road
- [ ] 8.11 Audio pass: engines, tires, crashes, horns, cabin
- [ ] 8.11a Radio: stations by genre, talk and podcasts, hosts who warn about weather and traffic, original or independently licensed music
- [ ] 8.12 Whole-run balance

Done when: a stranger can play Miami to Jacksonville and understand the game. This is the demo and trailer build.

## M9 Co-op
- [ ] 9.1 Check the sim runs the same on a server as on a client
- [ ] 9.2 Second player as passenger: phone, navigation, distracting the driver
- [ ] 9.3 Second player in their own car on the same road, each with their own scenario

Done when: two friends finish a run together.

## M10 Remaining states
Georgia first (and Georgia State Patrol), then one state per milestone, in route order.

## M11 Release
Steam page, itch demo, performance on modest PCs, legal review, credits, launch.

## M12 Post-launch
More interstates, ghost races, open multiplayer highways, artist and podcast partnerships on the radio.

## Ideas list
- Optional manual transmission with a clutch
- Florida's Turnpike as an alternate route with toll plazas
- School-zone speed cameras on city streets off the exits
