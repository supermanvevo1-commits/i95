# Survive the I-95: production roadmap

The game is built in phases, the way studios do it:
1. **Prototype:** prove the core systems.
2. **Vertical slice:** make the Miami test road look, sound and play like the finished game.
3. **Production:** build the rest of the content to that standard.
4. **Demo and co-op**, then **release**.

Graphics and sound are not one late milestone. The slice sets their target on one road, and production makes everything else match it.

How each step is worked:
- One milestone at a time. Each step is tested live in the editor, then committed.
- Before a step is checked off, it gets a second look. Does it behave like real driving and real drivers on that road? Does it still make sense next to the steps before it? If not, it's reworked, and earlier steps are reopened when needed.
- Visual steps come with before-and-after screenshots from the same spot.
- A milestone closes only after the creator plays it and every "Done when" item passes.
- New ideas go in the Ideas list, not the current milestone.

# Phase 1: Prototype (done)

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
- [x] Import Downtown Miami to Fort Lauderdale as the test road
- [x] Elevated highway sections and bridges with height
- [x] 12 drivable exits with one engineered shape: speed-change lanes, clean splits, wide curves and merges
- [x] Exit cues: road arrows, striped split areas, advance, split, merge, ramp speed and speed limit signs
- [x] First-pass skyline and buildings from real map footprints
- [x] Sound walls and urban roadside

Done when: it reads as Miami from the driver's seat and holds its frame rate.

## M3 Traffic (done)
- [x] 3.1 Spawn and remove cars around the player
- [x] 3.2 Lane keeping and following distance
- [x] 3.3 Placeholder models from the vehicle catalog
- [x] 3.4 Lane changes with signals
- [x] 3.5 Trait system (attention, aggression, lane discipline)
- [x] 3.6 First drivers: normal, left-lane camper, tailgater, trucks
- [x] 3.7 Collisions between traffic and the player
- [x] 3.8 Distant-traffic simplification and a 40-car performance check

Done when: 40 cars run smoothly, no crashes happen without a cause, and a camper is recognizable.

# Phase 2: Vertical slice (current)
Ten minutes on the Miami test road that look, sound and play like the finished game. Milestones run in this order.

## M4 Road life (current)
Drivers with memory and character, and real speed limits.
- [x] 4.1 Regulars: drivers you have a run-in with stay in the run with the same car, keep driving out of sight, and can catch up later
- [x] 4.2 Grudges: cutting off, brake-checking, tailgating, holding up or hitting someone makes them angry, and they cool off slowly
- [x] 4.3 Road rage that escalates: horn and high beams, riding your bumper, pulling alongside to yell, cutting in and brake-checking, ramming
- [x] 4.4 Short on-screen lines when it matters ("The red pickup truck is back.")
- [x] 4.5 Temperaments from real survey odds: most drivers shrug it off or honk, few follow, very few ram
- [x] 4.6 Playtest notes: narrower field of view, mirror glances, look back, mouse-wheel cruise control, GTA-style keys, horn
- [x] 4.7 Real speed limits per section. Drivers mostly run 5-9 over, a few far over in any lane
    - Second look: in the 65 zone, measured traffic averages +3 over with the 85th percentile at +8 (real roads: about +5 and +9-10). About half of drivers are held behind slower cars, and the 55 zone downtown sags to the limit. Revisit traffic flow in M6 (rush hour)
- [x] 4.7b Lanes sort by speed like a real highway: slower drivers keep right, faster ones settle left, so the left lane runs noticeably quicker. Its pace varies with who's leading, and now and then one driver is flat-out booking it
    - Second look: each driver settles in a lane by speed habit (12+ over lives left). Over 3 one-hour runs, the two left lanes average +4.4 to +5 over and the right lanes +1 to +2. Before this step the left lane was +2 and slower than its neighbor. The fastest drivers reach +35 to +42 in any lane. Campers stay a few over (0-5) and still plug the left lane, so fast drivers often pass them on the right, as in real life. Two-thirds of campers move over if someone rides their bumper for 10-45 s, but that rarely happens because people pass instead. 0 unprovoked or chain crashes. The only stops were a camper towing a boat waiting about 15 s for a gap where its lane ended
- [x] 4.8 The car says something about the driver, as odds, never certainties. Covers every vehicle on the road: minivans calm (a rare crash-out, not never), sports and muscle cars race and take risks, luxury cars own the road, lifted trucks on oversized wheels push people around, plus work vans, taxis, rentals, RVs and snowbirds, trucks, motorcycles. Adds the lifted truck
    - Second look: over two 2-hour runs (about 1,000 drivers each), the profiles separate clearly. Families: temper 0.6, +1-2 mph over, 2.25 s gaps. Lifted trucks: temper 2.2, +6-7 over, 1.75 s gaps. Snowbirds and RVs run 2-3 under. 0 unprovoked or chain crashes. Rentals don't yet make late exit moves, because traffic doesn't take exits yet
- [ ] 4.8b Who's behind the wheel: age, experience and license, from real data. Skill comes from experience, not the car. Teens crash far more per mile (IIHS: 16-19 nearly 3 times drivers 20+). Drivers 70+ rise again per mile, mostly from fragility, not mistakes. About 1 in 5 fatal crashes involves an unlicensed or invalidly licensed driver (AAA Foundation), with suspended or revoked licenses about 5-7% of drivers in them. The car then shifts the odds by who owns it. An older luxury owner is careful and values the car; a young driver in the same car takes risks. Japanese and Korean commuter cars mean disciplined owners, but a cheap tuner hatchback with a young driver means impulsive. Low-skill drivers wander in the lane and drift over the line, brake late and hard, merge sloppily, and signal late or never. This revisits 4.8, where luxury drivers got a flat, low discipline, so it depends on age instead
- [ ] 4.9 License plates from where the driver is from (Florida locals, Georgia and Carolina through traffic, New York and New Jersey hurry, Ontario and Quebec snowbirds), hinting at how they drive and helping you recognize regulars. Who's on the road depends on where and why: more freight near ports and Jacksonville, more RVs, snowbirds and Quebec plates heading to Florida in winter, commuters and delivery vans near cities, beach and vacation traffic in season

Done when: a driver you wronged comes back and you recognize them, and you can tell a minivan mom from a lifted-truck guy by how they drive.

## M5 Look, sound and screen target
Sets the target for how the whole game looks and sounds, on the Miami test road, in daylight. Bar: at least Midnight Club II, aiming toward GTA V over time.
- [ ] 5.1 Lighting and atmosphere: sun and sky, haze in the distance, color grading, bloom, soft shadows, ambient occlusion
- [ ] 5.2 Road and roadside: real asphalt and concrete textures, worn paint, patches and slab joints, concrete Jersey barriers and guardrails, steel sign gantries
- [ ] 5.3 Signs built like real US highway signs: green reflective panels with a white border and rounded corners, a highway-style font (a free, openly licensed lookalike of the official one), route shields (I-95), exit number tabs, control cities and distances, plus billboards with designed satirical ads. The full set a driver reads: overhead gantries naming the road and control cities, distance signs ("Fort Lauderdale 12"), exit and merge signs, mile markers (exit numbers match them), and the small reference markers every tenth of a mile that tell 911 and tow trucks where you are
- [ ] 5.4 Buildings with textured walls, windows and roofs, so the skyline isn't flat boxes
- [ ] 5.5 HUD in one visual style: a live analog speedometer as in Forza, with a needle and both mph and km/h, plus gear, cruise and damage, on designed panels with a proper font. Shown in third person, and its corner can be moved in settings
- [ ] 5.6 Speed limit sign in the corner, as in a map app, that turns red and pulses when you're over
- [ ] 5.7 Title card at the start, and "Welcome to Miami" style cards for cities and states
- [ ] 5.8 Styled notices and a minimap
- [ ] 5.9 Core sounds from real recordings: engines by vehicle class, tires, wind, and horns that fit the vehicle (sedan beep, truck air horn)

Done when: the creator calls the screen polished, side by side the game looks at least as good as Midnight Club II, and nothing sounds synthetic.

## M6 Road events
Places where traffic piles up for real reasons.
- [ ] 6.1 Distracted drivers: about 3 in 100 on their phone, drifting and reacting late. Honking at one can snap them out of it
- [ ] 6.2 Accidents between other drivers in real proportions (rear-end, sideswipe, run off the road, debris, rare freak events like a gator or a spill), blocking lanes, with rubbernecking in both directions
- [ ] 6.3 Construction zones: lane closures, merges, lower limits, and cones and barrels you can hit and scatter
- [ ] 6.4 95 Express lanes: pay-by-plate toll lanes that cost more as traffic builds. Pay to skip the jam, or cut through the poles
- [ ] 6.5 Rush hour: morning and evening peaks with stop-and-go jams from volume alone, and cheaper distant cars so a jam can hold hundreds

Done when: a jam forms and clears believably, and the frame rate holds in a jam.

## M7 Vehicles and feel
Every vehicle drives like what it is. The same stats drive the player and traffic.
- [ ] 7.1 A stats sheet for every vehicle from real specifications: top speed, 0-60, 60-0 braking distance, cornering grip, weight, wheelbase, steering speed, handbrake, fuel tank and mileage
- [ ] 7.2 Weighty feel in the spirit of GTA IV: soft suspension, body roll and dive, weight that shifts in corners and under braking, momentum you have to manage. Our own model, tuned per vehicle. The road talks back: concrete slab joints thump, patches and dips move the car a little
- [ ] 7.3 Variety inside a class: a family sedan and a sport sedan feel different
- [ ] 7.4 Heavy vehicles: slow to speed up, long to stop, lean, trailers sway
- [ ] 7.5 Motorcycles: quick, light, lean into turns, fragile in a crash
- [ ] 7.6 Traffic accelerates, brakes and corners by the same stats

Done when: a sedan, a pickup, a semi and a motorcycle each feel clearly different to drive.

## M8 Police and traffic law
Florida law first. Police are part of traffic, not only a chase.
- [ ] 8.1 Florida rules the game checks: speed tiers (100+ mph or 50+ over is criminal), Move Over, hands-free phone law, left lane, doubled fines in work zones with workers
- [ ] 8.2 Patrol cars in traffic, hidden in medians and behind overpasses. Everyone slows near them
- [ ] 8.3 Police pull over AI drivers too, with lights on the shoulder, and Move Over applies
- [ ] 8.4 Being noticed: a trooper sees you, a plate reader or aircraft spotter flags you, other drivers call you in
- [ ] 8.5 Traffic stops: the officer walks up and talks, you pick answers, the officer runs your plate. Wait it out or run
- [ ] 8.6 Heat, tickets and their cost
- [ ] 8.7 Pursuit driving, PIT maneuver, roadblocks and spike strips, and bystanders who get caught in it
- [ ] 8.8 Busted and escaped outcomes

Done when: a chase can be won or lost, a stop can be talked through, and the road shows police presence without a chase.

## M9 First full run (slice gate)
One scenario, the road trip, playable from menu to finish on the Miami test road.
- [ ] 9.1 Gas by real tank size and mileage, with a station on the test road
- [ ] 9.2 Money: start funds, gas, food, tickets, repairs
- [ ] 9.3 Bathroom, food and sleep at a basic level
- [ ] 9.4 Main menu with scenario, season and car select, pause, save and load
- [ ] 9.5 Breakdowns and wrecks cost time, money and stress, not the run. Limp to the shoulder or the next exit on a damaged car. Quick fixes yourself (spare tire, taping a bumper). Florida's Road Rangers, the free FDOT patrol on urban interstates, show up for small problems. Call a tow with the phone and wait on the shoulder while traffic and police roll past. A mechanic in the next town costs money and clock hours. Roadside membership makes tows cheaper. The instant R reset stays as a test key only

Done when: someone who has never seen the game (Crispy, Noah) plays the slice without help, and wants to keep going.

# Phase 3: Production (Florida)

## M10 Lives on the road
Every car is someone with somewhere to be. The story comes out of the systems.
- [ ] 10.1 Every AI driver has a life: who they are, where they're going, what they carry, and how they drive because of it
- [ ] 10.2 An event director that spaces out incidents and lets them feed each other
- [ ] 10.3 Mood and stress for your character
- [ ] 10.4 The phone: first-person texting mini-game, with the risk of being seen
- [ ] 10.5 Scenarios: family with kids and a crying baby, trucker, smuggler, cannonballer (unlocked with money and upgrades), free drive
- [ ] 10.6 A passenger who can take the wheel while you sleep, driving by their own skill
- [ ] 10.7 Animated people in the car: the driver steers and shifts, a partner in the passenger seat, kids in the back, reacting to the road

Done when: three scenarios feel like three different games, and an AI driver's story visibly causes trouble for you.

## M11 Time and weather
- [ ] 11.1 Day and night: a Miami night look with lit street lamps, lit windows and neon, plus headlights
- [ ] 11.2 Rain, wipers, windshield drops, wet grip, hydroplaning and spray
- [ ] 11.3 Seasons, chosen per run, and per-state weather odds (STATES.md): storms, lightning, fog, smoke, glare, flash floods
- [ ] 11.4 Rare big events, warned about on the radio first: hurricane evacuation, tornado
- [ ] 11.5 Snow and ice for the northern states and winter runs
- [ ] 11.6 NPC weather behavior: slowing, hazards on, pulling under overpasses, spinning out

Done when: a Florida downpour changes how you drive, and a hurricane evacuation feels like an event.

## M12 Miami to Jacksonville (demo)
The whole Florida route, built to the slice's look and sound. See FLORIDA_ROUTE.md.
- [ ] 12.1 Road generator handles interchanges, ramps and lane changes, with real speed limits
- [ ] 12.2 Compression: empty stretches shortened about 3 to 1, cities near real size, landmark order kept (Florida about 2 hours)
- [ ] 12.3 Bridges with height and water
- [ ] 12.4 Build sections in route order, one at a time
- [ ] 12.5 Regional scenery: palms, swamp, coastal, pine
- [ ] 12.6 Gas stations and rest stops at real spacing, with pull-off ramps, signs, and exits that reach city streets
- [ ] 12.7 Florida events: gators, downpours, Florida Man
- [ ] 12.8 Full NPC behavior list (npc_behaviors.md)
- [ ] 12.9 Art for everything: fictional car designs with parody brand names and no real badges, as GTA does, roadside variety, proper trees
- [ ] 12.10 Full sound: every vehicle, ambience, cabin, and the radio (stations by genre, talk and podcasts, original or independently licensed music)
- [ ] 12.11 Settings screen, performance on modest PCs
- [ ] 12.12 Whole-run balance

Done when: a stranger can play Miami to Jacksonville and understand the game. This is the demo and trailer build.

# Phase 4: Co-op and the rest of I-95

## M13 Co-op
- [ ] 13.1 Check the sim runs the same on a server as on a client
- [ ] 13.2 Second player as passenger: phone, navigation, radio, distracting the driver, taking the wheel
- [ ] 13.3 Second player in their own car on the same road, each with their own scenario

Done when: two friends finish a run together.

## M14 Remaining states
Georgia first (and Georgia State Patrol), then one state per milestone, in route order, to Houlton, Maine. Each state gets its limits, weather, police, tolls and character from STATES.md, checked against current sources.

# Phase 5: Release

## M15 Release
Steam page, itch demo, legal review (car lookalikes, music licenses), credits, launch.

## M16 Post-launch
More interstates, ghost races, open multiplayer highways, artist and podcast partnerships on the radio.

## Ideas list
- Optional manual transmission with a clutch
- Florida's Turnpike as an alternate route with toll plazas
- School-zone speed cameras on city streets off the exits
- A Montreal-to-Miami scenario start: Quebec plates, the long drive south
- Druidus (Crispy's band) as a first artist on the radio, with a written license
- End-of-run recap: the story of your trip ("the gray sedan chased you for 14 miles"), made to be shared
- Photo mode and crash replays, for clips and screenshots
- Streamer mode: Twitch chat can send events onto the road (a Florida Man sighting, a sudden downpour)
