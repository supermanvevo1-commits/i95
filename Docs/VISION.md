# Survive the I-95: vision

Status: the creator's direction for the whole game, written down on 2026-11-10. The roadmap places each part in a milestone. The NPC wishlist is in npc_behaviors.md.

## The idea
The word for it is sonder: realizing that every passerby has a life as vivid as your own. The game started as a race to Jacksonville. It became surviving the I-95 to wherever your own story needs you to be. The race is one scenario (the cannonballer); survival is the thread through all of them.

Every car on the highway is a life with somewhere to be. One driver is on a road trip with a girlfriend, one has three screaming kids, one is a trucker on a deadline, one is smuggling something and cut corners getting ready, one is a cannonballer in a supercar with extra fuel in the back. You play one of them. The others are AI now and other players later. Their stories collide: the smuggler who skipped the prep runs from a stop, the trooper swerves after him, and you're the one who gets clipped and sits in the jam.

It's a driving life simulator in the way Death Stranding is a walking simulator, and the story comes out of the systems the way it does in RimWorld. No two runs are the same.

## Decisions so far
- A full Miami-to-Maine run is about 10-12 hours of play over several sessions, saved at rest stops. Florida is about 2 hours. Rural stretches are shortened about 3 to 1. Cities stay close to real size.
- Seasons are chosen at the start of a run, not taken from the computer's clock. A shared multiplayer world could follow the calendar later.
- Third person is allowed everywhere. Texting is first person only.
- Every car has cruise control.
- Controls follow GTA's keyboard-and-mouse layout: W/S, A/D, Space handbrake, V camera, C look back, E horn. Additions: mouse wheel for cruise speed with gentle braking on the way down, front side button to floor it, back side button to brake, right-click for mirrors, Z and X for turn signals, H to flash the high beams, G for hazards, F2 to move the HUD to another corner (until the settings screen).
- Driving feel (creator, 2026-11-10): fun first, in the spirit of GTA V, GTA IV and Midnight Club, but grounded. From how players describe those games: GTA IV is heavy, with soft suspension and a lot of body lean, and is harder to control; GTA V is responsive and forgiving, and some miss IV's weight. The target is GTA V's control with GTA IV's weight. On keys, a tap nudges and a held key builds to the hardest turn the tires can make, and stability control (in every US car since 2012) catches the tail. The weight, lean and per-vehicle differences come in M7. Sources: [Steam discussion](https://steamcommunity.com/app/271590/discussions/0/610573009239529715), [The Drive](https://www.thedrive.com/news/gta-6-driving-physics-hint-at-return-to-the-realism-of-gta-4), [5mods handling guide](https://github-wiki-see.page/m/5mods/tutorials/wiki/Handling-%E2%80%90-Understanding-and-editing-GTAV's-Handling).
- Time of day is chosen at the start of a run (day, night, or a clock that moves as you drive), like the season.
- Getting hurt: a hard hit can concuss or daze your character depending on where the car was struck (rear hit: whiplash; front or side: concussion). It blurs and sways the view until treated: medicine at the next stop, or wait for an ambulance, which costs time and money. Rest at a stop heals a light daze slowly for free (creator, 2026-11-11). Never the end of the run.
- Motorcycles are the trade-off vehicle: nimble enough to split lanes through a jam, but a hit that dents a car kills a rider. Lane splitting is illegal in Florida and most states on the route, so police attention for it goes in the per-state rules.
- Big disasters and the response to them (creator, 2026-11-11): a fuel tanker can, very rarely, crash and burn, hurting the people around it and jamming the road for miles. Troopers, fire engines, ambulances, tow trucks and a hazmat crew arrive and clear it the way they really do (roadmap 6.7). Pursuits that cars can't end bring a police helicopter, sooner for cannonballers and for a wrecked car that keeps running (8.7b). Speed feel is grounded: the camera shakes from the road, not at random (5b.4). Landscapers' trailers and tilted tow-truck beds make real ramps to jump (6.6).
- Driver and passengers are animated in the car: partner up front, kids in the back.
- The shareable game design document is the Claude Doc "Survive the I-95: Game Design".

## Who you can be (scenarios)
- **Road trip:** a partner in the passenger seat. They can take the wheel while you sleep. They may be a poor driver, or have no license, and that can get you pulled over.
- **Family:** kids and a crying baby in the back. Stress builds in traffic and after a crash, and it affects your character.
- **Trucker:** cargo jobs and deadlines. Big tank, poor mileage, the things that happen to truckers: weigh stations, hours on the road, a load that shifts, being cut off by cars that don't know how long a truck takes to stop.
- **Smuggler (runner):** what you prepared beforehand decides how a traffic stop goes: plates, paperwork, a clean car, a story that holds up. Skip prep and the officer finds out. Stay put or run. A buyer's deadline pushes you to speed, the one thing you shouldn't do while carrying. I-95 is a known drug corridor, and police really do run interdiction on it: a small violation (a tag light, following too close, 4 over) becomes the reason to look closer, and troopers watch for tells: driving exactly the limit, eyes locked ahead, a fresh out-of-state rental, a car riding low. The cargo matters: heavy loads make the car sag, some cargo a K9 can smell, and a hidden compartment costs money up front but survives a quick search.
- **Cannonballer:** unlocked with money and car upgrades (engine, fuel tank, auxiliary fuel). The fastest, the most heat.
- **Medical courier (creator, 2026-11-11):** an organ or blood shipment in a cooler that has to arrive within hours. A legal reason to speed; a trooper may escort you or may not believe you.
- **Moving truck:** a family's whole life in a rental box truck towing their car on a dolly. Heavy, slow, can't brake, merge or back up like a car, and a wind gust on a bridge is felt.
- **Rideshare driver:** passengers picked up and dropped along the route, ratings, and some passengers who are trouble.
- **Tow or repo driver:** you go toward the wrecks instead of away from them, racing other tow trucks to the call, and repossessing cars from people who don't want to give them up.
- **Just driving:** no objective, only the road.

## Loadout: choices before the run (creator, 2026-11-11)
Every scenario starts on one screen where you choose what you go with, and every choice is a trade: the job's pay (or the trip's budget) is what you spend it from, so the better car or driver leaves less profit. The trucker picks a rig and a load, the family a car and how many kids, the road trip a partner, the runner a car and a driver.
- **Car:** a beater is cheap and blends in but can break down and won't outrun anyone; a rental is clean but an out-of-state rental is what troopers look for; a fast car can run but draws eyes.
- **Driver:** skill (better handling, calmer in a stop, costs the most); licensed or not (unlicensed is cheap, but any stop becomes an arrest); and habits that become needs to manage on the road:
  - A smoker gets jittery and irritable without cigarettes and drives more aggressively; fix it at a gas station.
  - A drinker is steady while drinking, but each drink slows reactions and adds drift, and a stop becomes a DUI and a search.
  - A texter keeps looking down at the phone (the chain-crash risk in 6.2).
  - Neglect a need and the driver goes erratic, and it shows in how the car drives.

## Modifications (creator, 2026-11-12)
Any car can be modified, but only with modifications that are really possible for that car: real parts, real gains, real costs. It mostly belongs to the scenarios that need it, like the cannonballer, street racer or runner. Grounded: a tune changes the numbers it would really change, including noise and how much police notice.

## The clock (creator, 2026-11-11)
By default the clock runs with the drive: time passes by the distance you cover, at the rate that makes the shortened route take real time (Florida is about 2 hours of play for a trip of about 5½ hours, so the clock runs about 2.75 times fast). Leave Miami at 3 p.m. and you reach Jacksonville somewhere around sunset to 9 p.m., depending on how you drove. Stops cost game time, so a long rest can push you into the night. A fixed day or night can still be chosen.

Night is when the road gets worse, from real data:
- About half of US traffic deaths happen at night on about a quarter of the driving; per mile, night is about three times as deadly.
- Drunk drivers peak from midnight to 3 a.m., worst on weekend nights. Florida allows sobriety checkpoints.
- Street racers and street takeovers, a real Miami night problem.
- Tired truckers, worst from 2 to 6 a.m.
- Fewer cars but faster ones, and unlit stretches outside the cities where you see only what your headlights reach.

## Leaving the highway
I-95 is the game. The city beside it is real to look at and you can drive its streets near an exit, but deliveries, stops and pickups happen a short drive from a real exit (a gas station, a rest area, a motel, a buyer's warehouse, a hospital dock). Beyond the band of streets built around each exit, the world ends naturally: canals, fences, dead ends, and police who notice a car circling a neighborhood. Staying on the highway is also simply faster, as in life: surface roads have lights every few blocks and 30-45 mph limits, and US-1 or A1A take hours longer. A parallel road (US-1, A1A) can be a deliberate choice later: slower, with lights and local police instead of troopers.

## Traffic stops
The officer walks up and talks. You pick answers. Wrong answers, or a car that isn't clean, send the officer back to check your plate. Then you wait and hope, or you floor it.

## Needs and money
Gas by real mileage and tank size (motorcycles: small cheap tank; sedans: standard; trucks: huge and expensive). Bathroom, food and sleep, as in American Truck Simulator. Prices change by state. Tickets cost money. Rest stops and gas are spaced as they really are.

## The phone
A first-person phone mini-game: you look down, read the message and pick or type a reply while the road keeps coming. Answer your girlfriend correctly. Look up in time. Other drivers can see you and call it in. AI drivers text too, and some of them cause the crash that jams everyone else. In co-op, a passenger player can hold the phone.

## Police and law
Real Florida traffic law first, then each state's. Police are visible on the road: they pull over someone else, and everyone around slows to the limit for a mile. A plate reader or an aircraft spotter can flag you, and a unit gets dispatched. Florida troopers drive regular cruisers. Georgia State Patrol is a different animal: fast, aggressive, and it uses the PIT maneuver freely, so crossing into Georgia is when you learn that.

## Radio
Several stations by genre, plus talk radio and podcasts, each with hosts who talk between songs, read the news, warn about weather and traffic ahead, and react to what happened on the road. It's a good way to warn the player that a storm or an evacuation is coming.
- At launch, music and shows are made for the game or licensed from independent artists.
- After launch, partnerships with artists and podcasters bring real songs and shows to their own stations.
- A real Spotify link would need Spotify's permission, and its terms limit using its music inside other apps. A curated, licensed station gives the same feeling and is fully under our control.

## Weather and states
Each state has its own speed limits, weather, police, tolls and character. See STATES.md. Weather is drawn per run by season, and rare events stay rare.

## Multiplayer
Co-op first (a second player as passenger or in another car), then shared highways. The sim already runs in plain C# with a fixed step and no scene objects, which is the right base for a server. Keep it that way.

## Screen and look
The HUD and on-screen text need a real design pass: clean type, a proper speedometer, styled notices, and a minimap that shows your scenario's destination. The art direction is semi-realistic. The target is a mix of two games: the grounded, readable highway world of American Truck Simulator (signs, traffic, lanes and laws), and the road feel and presentation of Need for Speed Underground 2 as its modern remaster mods show it (a road you feel through the car, wet reflections, drama in the light). The look target is set on the Miami test road in the vertical slice (M5), then matched across the route in production. Game code doesn't depend on it.

---

# Research notes for these systems

Sources are news, law and agency summaries gathered on 2026-11-10. Use them as tuning targets, not legal advice. Check the primary sources before any number is final.

## Crashes on I-95 in Florida
- Rear-end crashes are the most common on I-95, from following too closely, speeding, sudden braking and distraction. Lane departure and sideswipe crashes come next, often from lane changes without checking the blind spot. A News4Jax report says many of the ~1,000 crashes in St. Johns County over three years were lane departures.
- I-95 ranks second among Florida's most dangerous roads (346 deaths, 2020–2022). Florida had 380,666 reported crashes in 2024 (FLHSMV).
- Debris is common: sheet metal, tires and tire treads ("road alligators"), plywood, ladders, mattresses. In three months, Jacksonville logged 351 reports of objects on I-95 and I-295, including 38 ladders and 40 tires. AAA: debris caused ~200,000 crashes over four years nationally, and debris crashes are four times more likely on interstates. A steel-rod spill once flattened about 30 cars' tires on I-95.
- Rare: alligators on the road have caused fatal I-95 crashes. Also produce and cargo spills, fires, and wrong-way drivers.
- Game weights: rear-end ~45%, sideswipe and lane change ~25%, run off the road ~15%, debris ~10%, freak ~5%. Distraction causes a share of every type.

## Phones and driving
- At any daylight moment, 3.0% of US drivers are visibly handling a phone (NHTSA NOPUS 2023). Among 16–24 year olds it's 7.7%, the highest on record.
- Handheld phone use multiplies crash risk about 3.6 times (NHTSA, SHRP2 naturalistic study).
- 397 people died in phone-involved crashes in 2023, 12% of distracted-driving deaths. Most texters never crash on a given trip. That matches the creator's experience.
- Game: about 3 in 100 AI drivers on their phone at a time, more among young drivers. A texting driver drifts in the lane, reacts late and brakes late. Their crash risk is ~3.6 times a normal driver's, so most get away with it and a few cause the jam.

## Speed limits on the Miami to Jacksonville route (dated sources: check signs in map data)
- Miami: 45 mph coming off US 1, 55 south of downtown, 60 north of SR 112, 55 around the Golden Glades.
- Work zones drop limits (for example 65 to 55 in South Broward during express-lane work).
- Rural Florida interstate: 70 (the state maximum). Jacksonville: 60 near downtown, 65 to the south, 70 in rural Nassau County.

## Florida law that matters in play
- **Move Over:** move over a lane for police, emergency vehicles, tow trucks, sanitation, utility and construction vehicles, and since 2024 for any disabled vehicle with hazards on. If you can't, slow to 20 mph under the limit.
- **Hands-free:** handheld phone use while driving is banned (expanded 2025, per a legislative analysis), with strict rules in school and work zones.
- **Extreme speeding:** 50+ mph over the limit, or 100+ mph anywhere, is now a criminal offense, not a ticket.
- **Cameras:** Florida allows automatic speed cameras only in school zones (2023 law, over 10 mph, $100). On the highway, speed is enforced by troopers and by aircraft ("speed enforced by aircraft"). The game's "camera that snaps your plate" should be a plate reader or an aircraft spotter on the highway that sends a unit after you. Real school-zone cameras can appear on city streets.
- Work-zone speeding fines are doubled when workers are present.

## Tolls on this route
I-95 in Florida has no toll booths. Miami to Fort Lauderdale has 95 Express lanes: separated left lanes with overhead gantries that charge by SunPass or bill by plate, and the price rises with traffic. Florida's Turnpike, which runs parallel, has tolls. In the game, a toll is a choice: pay to dodge the jam, or crash through the plastic delineator poles and get billed or chased.

## Georgia State Patrol
An AJC investigation found GSP's pursuit policy among the most permissive of 44 state agencies. Troopers can chase for minor offenses and used PIT in more than 2,000 chases from 2019 to 2023, sometimes above 100 mph. GSP cites street racing and excessive speeding around Atlanta. Atlanta's own police limit PITs, but GSP isn't bound by those rules.
