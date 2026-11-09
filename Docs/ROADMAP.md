# Survive the I-95: production roadmap

One milestone at a time. Each step is tested live in the editor, then committed. A milestone closes only after the creator plays it and every "Done when" item passes. New ideas go in the Ideas list, not the current milestone.

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

## M2b Miami test road (current)
Move testing to where the game starts, so traffic and police are tuned in the real setting.
- [x] Import Downtown Miami to Fort Lauderdale as the test road
- [x] Elevated highway sections and bridges with height
- [x] Scenery on and off ramps from real map data
- [x] Drivable exits: 8 exits that leave I-95 and rejoin (road network that traffic will also use)
- [ ] Clean up exits 0 to 2 and add matching exit signs
- [x] First-pass skyline and buildings from real map footprints
- [x] Sound walls and urban roadside

Done when: it reads as Miami from the driver's seat and holds its frame rate.

## M3 Traffic
- [ ] 3.1 Spawn and remove cars around the player
- [ ] 3.2 Lane keeping and following distance
- [ ] 3.3 Placeholder models from the vehicle catalog
- [ ] 3.4 Lane changes with signals
- [ ] 3.5 Trait system (attention, aggression, lane discipline)
- [ ] 3.6 First drivers: normal, left-lane camper, tailgater, trucks
- [ ] 3.7 Collisions between traffic and the player
- [ ] 3.8 Distant-traffic simplification and a 40-car performance check

Done when: 40 cars run smoothly, no crashes happen without a cause, and a camper is recognizable.

## M4 Police and heat
- [ ] 4.1 Heat meter and what raises it
- [ ] 4.2 Patrol cars and being noticed
- [ ] 4.3 Pursuit driving
- [ ] 4.4 PIT maneuver
- [ ] 4.5 Roadblocks and spike strips
- [ ] 4.6 Busted and escaped outcomes
- [ ] 4.7 Per-state police profiles

Done when: a chase can be won or lost, and 5 minutes on the Jupiter stretch is fun.

## M5 Run loop
- [ ] 5.1 HUD
- [ ] 5.2 Gas and refueling
- [ ] 5.3 Money, fines and repairs
- [ ] 5.4 Fatigue and rest
- [ ] 5.5 Stops with pull-off ramps
- [ ] 5.6 Main menu, pause and car select
- [ ] 5.7 Save and load
- [ ] 5.8 Scenarios: why you're on the highway

Done when: one complete run works from menu to finish on the Jupiter stretch.

## M6 Weather and time
- [ ] 6.1 Day and night, with a Miami night look: lit street lamps, lit building windows, neon
- [ ] 6.2 Headlights
- [ ] 6.3 Rain, wipers and windshield drops
- [ ] 6.4 Wet grip, hydroplaning and spray
- [ ] 6.5 NPC weather behavior
- [ ] 6.6 Per-state weather tables

Done when: a Florida downpour changes how you drive.

## M7 Miami to Jacksonville
See FLORIDA_ROUTE.md for the map and section list.
- [ ] 7.1 Road generator handles interchanges, ramps and lane changes
- [ ] 7.2 Compression tool that shortens empty stretches, keeps landmark order
- [ ] 7.3 Bridges with height and water
- [ ] 7.4 Build sections in route order, one at a time
- [ ] 7.5 Regional scenery: palms, swamp, coastal, pine
- [ ] 7.5a City skylines and roadside buildings from real map building data
- [ ] 7.6 Florida events: gators, downpours, Florida Man
- [ ] 7.7 Full NPC behavior list
- [ ] 7.8 Art pass: fictional car designs, lighting, proper trees
- [ ] 7.9 Driver model in the cockpit with arms that steer and shift
- [ ] 7.10 Audio pass
- [ ] 7.11 Whole-run balance

Done when: a stranger can play Miami to Jacksonville and understand the game. This is the demo and trailer build.

## M8 Remaining states
Georgia first, then one state per milestone, in route order.

## M9 Release
Steam page, itch demo, performance on modest PCs, legal review, credits, launch.

## M10 Post-launch
More interstates, ghost races, multiplayer.

## Ideas list
- Optional manual transmission with a clutch
