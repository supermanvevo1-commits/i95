# Survive the I-95: vision

Status: the creator's direction for the whole game, written down on 2026-11-10. The roadmap places each part in a milestone. The NPC wishlist is in npc_behaviors.md.

## The idea
Every car on the highway is a life with somewhere to be. One driver is on a road trip with a girlfriend, one has three screaming kids, one is a trucker on a deadline, one is smuggling something and cut corners getting ready, one is a cannonballer in a supercar with extra fuel in the back. You play one of them. The others are AI now and other players later. Their stories collide: the smuggler who skipped the prep runs from a stop, the trooper swerves after him, and you're the one who gets clipped and sits in the jam.

It's a driving life simulator in the way Death Stranding is a walking simulator, and the story comes out of the systems the way it does in RimWorld. No two runs are the same.

## Who you can be (scenarios)
- **Road trip:** a partner in the passenger seat. They can take the wheel while you sleep. They may be a poor driver, or have no license, and that can get you pulled over.
- **Family:** kids and a crying baby in the back. Stress builds in traffic and after a crash, and it affects your character.
- **Trucker:** cargo jobs and deadlines. Big tank, poor mileage, the things that happen to truckers: weigh stations, hours on the road, a load that shifts, being cut off by cars that don't know how long a truck takes to stop.
- **Smuggler:** what you prepared beforehand decides how a traffic stop goes: plates, paperwork, a clean car, a story that holds up. Skip prep and the officer finds out. Stay put or run.
- **Cannonballer:** unlocked with money and car upgrades (engine, fuel tank, auxiliary fuel). The fastest, the most heat.
- **Just driving:** no objective, only the road.

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
The HUD and on-screen text need a real design pass: clean type, a proper speedometer, styled notices, and a minimap that shows your scenario's destination. The art direction is semi-realistic for now. The final look is decided at the art pass. Game code doesn't depend on it.

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
