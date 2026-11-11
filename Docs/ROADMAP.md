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

## M4 Road life (done)
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
- [x] 4.8b Who's behind the wheel: age, experience and license, from real data. Skill comes from experience, not the car. Teens crash far more per mile (IIHS: 16-19 nearly 3 times drivers 20+). Drivers 70+ rise again per mile, mostly from fragility, not mistakes. About 1 in 5 fatal crashes involves an unlicensed or invalidly licensed driver (AAA Foundation), with suspended or revoked licenses about 5-7% of drivers in them. The car then shifts the odds by who owns it. An older luxury owner is careful and values the car; a young driver in the same car takes risks. Japanese and Korean commuter cars mean disciplined owners, but a cheap tuner hatchback with a young driver means impulsive. Low-skill drivers wander in the lane and drift over the line, brake late and hard, merge sloppily, and signal late or never. This revisits 4.8, where luxury drivers got a flat, low discipline, so it depends on age instead
    - Second look: over two one-hour runs (about 420 drivers each), teens average skill 0.3, +4 to +9 over, 25-31% signal use and the tightest gaps. Adults: skill 0.72, +2 to +3, 76% signal use (studies put US drivers at about 75%). Seniors run at or under the limit with the widest gaps. Elderly drivers are less skilled but slow. Live test: a 17-year-old with skill 0.05 sways 0.92 m around the lane center, against 0.06 m for a skilled 45-year-old. Drifting onto the line is about once every 2 minutes for teens and once every 10 for adults, only toward a side with room for now; crashes caused by drivers come with M6. Signal use first came out at 55%, which is too low, so it was retuned. 0 unprovoked or chain crashes. License status is stored for the police (M8): suspended and unlicensed drivers will be the ones who run
- [x] 4.8c Controls from the creator's first playtest: mouse back button brakes (front button is gas), turn signals for the player, and smoother keyboard steering at speed (A and D ease in, and steer less the faster you go) so 60+ mph lane changes don't snap the car loose
    - Second look: the cause was a key asking for full lock, about 3 g at 60 mph, three times the tires' grip. Keyboard steering now asks for half the grip on a tap and up to 0.8 for a long hold. Gamepad stays analog, and the handbrake still allows full lock. At 62 mph a 0.5 s tap: before, it slid at 1.0 g; after, it grips at 0.5 g with 1° of slip. A 1.2 s hold: before, it slid into the wall; after, it grips at 0.63 g. Z and X toggle the signals, which cancel themselves after the lane change. H flashes the high beams. The placeholder on-screen markers wait for the HUD in M5. The player car's own blinker lights aren't drawn yet
- [x] 4.8d Getting out of the way: drivers notice a faster car closing from behind (how soon depends on attention and skill; towing a boat or hauling a box blocks the mirror) and move over. Riding their bumper, flashing high beams (H) and leaning on the horn raise the pressure. A hot-tempered driver brake-checks you instead
    - Second look: closing at 80 on a sedan doing 60, it moved over after 4-8 s; with horn and high beams, after 2 s. A boat-towing SUV took 8.6 s, and 2.2 s with the horn. The single test cars had random attention, so the mirror effect shows in the odds rather than one run. Leaning on the horn for 2.5 s is now a grievance of its own, and most drivers shrug it off. Campers wear down 2-3 times faster under horn and high beams
- [x] 4.8e Fair cut-off calls: a lane change only counts as cutting someone off if you take a gap that's too tight for their speed. Signaling first softens it, and merging alongside with room is fine
    - Second look: a cut-off now needs under 0.9 s of following distance left for them (0.6 s if you signaled for 0.8 s or more first), and it doesn't count if you're pulling away fast. Tests: tight with no signal angered them to level 2; tight but signaled was a milder grudge; 22 m signaled and 45 m unsignaled were no grudge at all, and there was no contact in any of them
- [x] 4.9 License plates from where the driver is from (Florida locals, Georgia and Carolina through traffic, New York and New Jersey hurry, Ontario and Quebec snowbirds), hinting at how they drive and helping you recognize regulars. Who's on the road depends on where and why: more freight near ports and Jacksonville, more RVs, snowbirds and Quebec plates heading to Florida in winter, commuters and delivery vans near cities, beach and vacation traffic in season
    - Second look: over one-hour runs, winter plates are about 59% Florida, 13-15% Quebec and Ontario, and 6% New York, with snowbirds 7-9% of drivers. Summer is 73% Florida, 3% Canadian and 1% snowbirds. Rentals and work vans carry Florida plates, and freight comes from everywhere. New York, New Jersey and Massachusetts drivers run a little faster and pushier, and out-of-towners are a little less sure of the road. Regulars are now named with their plates ("the gray sedan with New York plates"). The test road is all city, so the city side of the mix (more taxis and vans, fewer RVs) is always on; rural stretches arrive with the full route (M12). The season defaults to winter until the menu lets you pick (M9)
- [x] 4.8f Keyboard steering redone after the second playtest (the creator found 4.8c had killed the car's ability to turn and dodge). A tap is a nudge and a held key builds in about half a second to the hardest turn the tires can make. Stability control, which every US car since 2012 has, catches the tail when it steps out, so a hard swerve no longer spins the car. The handbrake still overrides it, and a muscle car has only half of it
    - Second look: measured on a test bench that runs the three versions side by side. Dodging one lane over: the first version (full lock on any key press) spun the car at 60 mph and above; 4.8c took 1.4 s at 60 mph and 1.7 s at 115; the new one takes 1.0 s at 60 and about 1.15 s at 115, at 1 g with 6° of slide and no spin. A real emergency lane change is about 1.5 s at 0.7 g for an average driver, so this is the car's limit, not an arcade cheat. In the game: 0.35 s right then 0.3 s left at 60 mph moves exactly one lane at 0.67 g; a 1.5 s hold at 40 mph turns the car 48° at 1 g. 4.8c had capped a held key at a quarter of a g at 115 mph. 0 unprovoked or chain crashes in a 30-minute traffic run
- [x] 4.8g Races that happen (second playtest: racers said they wanted to race and then never showed up). Nothing to press: a sporty driver you blow past chases you. They now accelerate like a sports car, run up to 150 mph, stay in it until 500 m apart, and race for 50-90 s instead of 12-25. Slowing below the speed limit ends it. The game says who won
    - Second look: the cause was that a racer gave up once you were 140 m ahead, and you were already pulling away at 45 mph when it began. They were also capped at 110 mph. Live test: passed a coupe at 115 mph; it went from 72 to 126 mph, caught up in 20 s, passed, and the game announced "You lost to the tan coupe with Ontario plates." A race is still only a chase; stakes, a finish line and a way to accept or decline belong with scenarios (M10)

Done when: a driver you wronged comes back and you recognize them, and you can tell a minivan mom from a lifted-truck guy by how they drive.

## M5 Look, sound and screen target (current)
Sets the target for how the whole game looks and sounds, on the Miami test road, in daylight. Bar: at least Midnight Club II, aiming toward GTA V over time.
- [x] 5.1 Lighting and atmosphere: sun and sky, haze in the distance, color grading, bloom, soft shadows, ambient occlusion
    - What changed: a clear blue Florida sky with scattered clouds replaces the grey overcast one. Filmic tone mapping, a light color grade and a subtle bloom are on (none were before). Shadows reach 300 m instead of 50 m, so buildings, walls and palms cast them. Edges are smoothed (4x). The whole look lives in one data file (`looks.json`), so night and rain can be added as more looks later (M11)
    - Second look: the sun stands 48° high in the southwest, which is Miami in mid-March at mid-afternoon (about a quarter to four on the clock), inside the snowbird season the game starts in. Its direction is measured from the sky picture, and a screenshot aimed at it puts the sun dead center, so shadows and sky agree. Haze matches about 8 miles of visibility, a normal humid Florida day: a building 1 km away is 26% faded, 2 km 45%, 3 km 59%. The first version made everything in shade navy blue and about 3 times too dark. The cause was a color-space mistake, found by measuring the shade light, and fixed; shade is now lit blue-grey by the sky from above and warm from the ground below. The grass tint was neon in full sun and was toned down. Frame rate: 156 before, 151 parked and 145 driving at 70 mph with 40 cars after
    - Still wrong, for later steps: buildings are flat colored boxes (5.4). The asphalt's cracks repeat and the elevated road casts no shadow on the ground under it (5.2). The player car's white paint looks chalky and flat. Lamp post shadows are too thin to see. Clouds don't move, and driving north the sun is always behind you, so there is no glare yet (M11)
- [ ] 5.2 Road and roadside: fix the median barrier's chopped, pixelated look seen from the left lanes (texture filtering), real asphalt and concrete textures, worn paint, patches and slab joints, concrete Jersey barriers and guardrails, steel sign gantries
    - Done so far (second playtest): the median and roadside barriers are now concrete Jersey barriers. They have crisp edges, a real concrete surface, a joint every 6 m, rain streaks, grime along the foot and black tire scuffs. The old one was a smooth ribbon with a small stretched texture, which broke into chunky pixels in a small window. Before and after from the left lane at the start are in `.livebridge/shots/m5_2_compare_median.png`
    - Still to do: asphalt (the cracks repeat), worn paint, patches and slab joints, guardrails, steel gantries, and a shadow under the elevated road
- [ ] 5.3 Signs built like real US highway signs: green reflective panels with a white border and rounded corners, a highway-style font (a free, openly licensed lookalike of the official one), route shields (I-95), exit number tabs, control cities and distances, plus billboards with designed satirical ads. The full set a driver reads: overhead gantries naming the road and control cities, distance signs ("Fort Lauderdale 12"), exit and merge signs, mile markers (exit numbers match them), and the small reference markers every tenth of a mile that tell 911 and tow trucks where you are
    - Done so far (second playtest: words ran off the signs at exits 3A, 10A, 18 and 24): every sign panel is now sized to its words, as real signs are. Green guide signs have a white border, rounded corners, an exit number tab on the top right corner, a highway-style typeface (Overpass, open license) and Florida-style short names ("NW 2nd St"). Yellow and white signs have black borders, speed limits have large numerals, mile markers stack their digits. Signs stand plumb
    - Second look: an automatic check (`signcheck`) measures every line of text against its panel: 233 signs, 454 lines, 0 outside. One billboard had lost its second line. The cause was signs tilting with the road's slope while their words were placed straight up, so lower lines sank behind the panel; it was the same for every sign on a hill. Lettering is about a third larger than real (0.58 m capitals against 0.4-0.5 m), because a screen shows far less detail than an eye and the words must be readable in time at speed; even so they only become readable about 90 m out at this window size
    - Still to do: overhead gantries over the lanes (the real fix for reading signs in time), route shields, distance signs, tenth-mile markers, designed billboard ads, and moving billboards that stand behind buildings
- [ ] 5.4 Buildings with textured walls, windows and roofs, so the skyline isn't flat boxes
- [x] 5.5 HUD in one visual style: a live analog speedometer as in Forza, with a needle and both mph and km/h, plus gear, cruise and damage, on designed panels with a proper font. Shown in third person, and its corner can be moved in settings
    - Second look: an analog dial with mph outside every 20 and km/h inside every 40, ticks every 5 mph, a red band past 120, and a weighted needle. The digital speed and gear (from the gear lever) sit in the lower half where the needle never goes. Two marks on the rim: amber at the posted limit, green at the cruise setting. Chips for cruise, high beams and damage, and green signal arrows that show only while blinking, like a real dash. Font: Barlow Semi Condensed (open license). Sized to the screen height, so it looks the same in a small window and at 4K. In the cockpit the dial hides (the car's own gauges show speed) and the chips, arrows and limit sign stay. F2 moves it between the four corners and remembers it; the settings screen (12.11) will take that over. Road notices ("The red pickup truck is back.") now use the same panel style, which is half of 5.8. 158 fps with it on screen. A full-view screenshot command (`screen`) captures the HUD; `shot` renders only the 3D view
- [x] 5.5b The player car's own lights on the model: blinkers when you signal, brake lights, high beams, hazards. Traffic cars already have them because they're built in code; the player car is an imported model, so its lamps need to be found and lit
    - Second look: the model's tail lens, headlight covers and amber parts are each one mesh across both sides, so at start they are split into left and right halves that light separately and still crumple in a crash. A lit lamp is exactly the shape of its lens. Brake lights come on with the brake pedal, including cruise control's gentle braking, as in a real car. The rear signal flashes the red tail lens, as on most US cars, and on the braking side it blinks between bright and off. The front signal is the outer corner of the headlight cover, because the amber part in the model sits hidden behind the cover. The red bumper reflectors stay dark. Hazards toggle with G. Lamps flash 90 times a minute (US law allows 60 to 120). Lit colors are bright enough to read in daylight; the first red tone-mapped to orange and was deepened. Traffic now uses the same lit colors. Lit headlights in daylight look like a plain white lens with no glow around it; that is normal in daylight
- [x] 5.6 Speed limit sign in the corner, as in a map app, that turns red and pulses when you're over
    - Second look: a US speed limit sign beside the dial shows the real posted limit where you are. Most drivers run 5-9 over (4.7), so a sign that went red at 1 over would be red all the time. It turns red at 10 over, roughly where Florida troopers start writing tickets, and pulses from 20 over. The digital speed turns orange at the same point. It hides on exit ramps until ramps carry their own limits
- [x] 5.7 Title card at the start, and "Welcome to Miami" style cards for cities and states
    - Second look: "SURVIVE THE I-95 / Miami to Houlton, Maine, 1,908 miles" (the FHWA length of I-95) shows at the start, then the place you're in. Crossing into a new place driving north shows "Welcome to" with the city and county: Hallandale Beach at the Broward County line, Hollywood at Pembroke Road, Dania Beach at Stirling Road, Fort Lauderdale past exit 24. Lines are set at the nearest exit to the real city limit, in `Routes/miami_ftl_places.json`; states use the same card when the route reaches Georgia. A reset or a jump doesn't show a card, and backing over a line is silent. A soft dark band keeps the words readable over bright clouds. Found while testing: the HUD's font had never reached the scene, so every earlier HUD screenshot was in the default font; fixed
- [ ] 5.8 Styled notices and a minimap
    - Done so far: notices use the HUD's panel style and font (see 5.5). Still to do: the minimap
- [ ] 5.9 Core sounds from real recordings: engines by vehicle class, tires, wind, and horns that fit the vehicle (sedan beep, truck air horn)
- [ ] 5.10 Controllers: gamepad works today (analog triggers and stick); add full button mapping, plus steering wheels and pedals (force feedback where the wheel supports it)

Done when: the creator calls the screen polished, side by side the game looks at least as good as Midnight Club II, and nothing sounds synthetic.

## M6 Road events
Places where traffic piles up for real reasons.
- [ ] 6.0 Traffic crash physics: a hit car spins, stops, smokes and shows damage, and the player can't drag it along or stick to it. Motorcycles go down at much lower speeds than cars, and heavy trucks barely move. A rider comes off the bike and tumbles (ragdoll), and a car at speed runs the bike over instead of stopping dead against it. Fixes the 99 mph side hit in the first playtest, where the other car shrugged it off
- [ ] 6.0b Angry chasers drive by skill: a skilled one weaves through traffic to reach you, flips you off and peels away; a hothead rides your bumper; a bad driver weaving after you can cause the crash that jams everyone, and everyone reacts by their own skill
- [ ] 6.0c Traffic cars that snap or rubber-band sideways during a lane change (seen once in the second playtest): find and smooth
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
- [ ] 7.5 Motorcycles: quick, light, lean into turns, fragile in a crash. They can split lanes between cars in a jam, which is their edge and their risk
- [ ] 7.6 Traffic accelerates, brakes and corners by the same stats
- [ ] 7.7 Optional manual transmission with a clutch, for sports cars and players who want it

Done when: a sedan, a pickup, a semi and a motorcycle each feel clearly different to drive.

## M8 Police and traffic law
Florida law first. Police are part of traffic, not only a chase.
- [ ] 8.1 Florida rules the game checks: speed tiers (100+ mph or 50+ over is criminal), Move Over, hands-free phone law, left lane, doubled fines in work zones with workers, and lane splitting on a motorcycle (illegal in Florida; how much police care is researched per state)
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
- [ ] 9.4 Main menu with scenario, season, time of day (day, night, or a moving clock) and car select, pause, save and load
- [ ] 9.5 Breakdowns and wrecks cost time, money and stress, not the run. Limp to the shoulder or the next exit on a damaged car. Quick fixes yourself (spare tire, taping a bumper). Florida's Road Rangers, the free FDOT patrol on urban interstates, show up for small problems. Call a tow with the phone and wait on the shoulder while traffic and police roll past. A mechanic in the next town costs money and clock hours. Roadside membership makes tows cheaper. The instant R reset stays as a test key only. Damage is by location: a front hit can kill the engine or the radiator, a side hit can bend a wheel, and that decides whether you can fix it yourself (change the wheel) or need a tow. Your character's skills (mechanic, driving) improve with use, so fixing it yourself gets faster over a run
- [ ] 9.6 Getting hurt: a hard hit can daze or concuss your character by where the car was struck (rear: whiplash; front or side: concussion). The view blurs and sways until it's treated with medicine from the next stop or by an ambulance, which costs time and money

Done when: someone who has never seen the game (Crispy, Noah) plays the slice without help, and wants to keep going.

# Phase 3: Production (Florida)

## M10 Lives on the road
Every car is someone with somewhere to be. The story comes out of the systems.
- [ ] 10.1 Every AI driver has a life: who they are, where they're going, what they carry, and how they drive because of it
- [ ] 10.2 An event director that spaces out incidents and lets them feed each other
- [ ] 10.3 Mood and stress for your character
- [ ] 10.4 The phone: first-person texting mini-game, with the risk of being seen
- [ ] 10.5 Scenarios: family with kids and a crying baby, trucker, smuggler, cannonballer (unlocked with money and upgrades), free drive. Cannonballers also appear as rare AI drivers running 100+ mph, and races get stakes, a finish line and a way to accept or decline
- [ ] 10.6 A passenger who can take the wheel while you sleep, driving by their own skill
- [ ] 10.7 Animated people in the car: in first person you see your own hands on the wheel, the shifter, the signal stalk and the phone; a partner in the passenger seat and kids in the back, reacting to the road
- [ ] 10.8 Stops you walk into: get out at a gas station or rest stop, go inside, pay the cashier for gas, buy snacks (snacks quiet the kids down), use the bathroom. Or pay at the pump with your card, where a sketchy station can skim it, and miles later your phone buzzes with a big charge you didn't make

Done when: three scenarios feel like three different games, and an AI driver's story visibly causes trouble for you.

## M11 Time and weather
- [ ] 11.1 Day and night: a Miami night look with lit street lamps, lit windows and neon, plus headlights. Chosen per run, or a clock that moves as you drive
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
- [ ] 12.11 Settings screen (field of view, key rebinding, volume, keyboard steering help on or off), performance on modest PCs
- [ ] 12.12 Whole-run balance

Done when: a stranger can play Miami to Jacksonville and understand the game. This is the demo and trailer build.

# Phase 4: Co-op and the rest of I-95

## M13 Co-op
- [ ] 13.1 Check the sim runs the same on a server as on a client
- [ ] 13.2 Second player as passenger: phone, navigation, radio, distracting the driver, taking the wheel
- [ ] 13.3 Second player in their own car on the same road, each with their own scenario
- [ ] 13.4 Difficulty and catching up. On Standard, the event director gives players who fell behind (after a wreck or a tow) a fair shot: more trouble lands ahead of the leaders (a jam, a speed trap, debris) and less on the trailing player. On Realistic there's no help: a wreck can sink your run, and you're hoping something happens up ahead. Vehicle choice and each player's objective weigh in either way

Done when: two friends finish a run together.

## M14 Remaining states
Georgia first (and Georgia State Patrol), then one state per milestone, in route order, to Houlton, Maine. Each state gets its limits, weather, police, tolls and character from STATES.md, checked against current sources.

# Phase 5: Release

## M15 Release
Steam page, itch demo, legal review (car lookalikes, music licenses), credits, launch.

## M16 Post-launch
More interstates, ghost races, open multiplayer highways, artist and podcast partnerships on the radio.

## Ideas list
- Florida's Turnpike as an alternate route with toll plazas
- School-zone speed cameras on city streets off the exits
- A Montreal-to-Miami scenario start: Quebec plates, the long drive south
- Druidus (Crispy's band) as a first artist on the radio, with a written license
- End-of-run recap: the story of your trip ("the gray sedan chased you for 14 miles"), made to be shared
- Photo mode and crash replays, for clips and screenshots
- An extreme, rare road-rage event: a maniac pulls a gun. Mature rating; it needs police (M8) to answer it, so not before then
- Streamer mode: Twitch chat can send events onto the road (a Florida Man sighting, a sudden downpour)
