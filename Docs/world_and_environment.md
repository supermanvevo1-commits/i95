# World and environment (accuracy to the real I-95)

Status: design target from the game's creator. Not implemented yet.

## Approach
- Road geometry from real data: I-95 route from OpenStreetMap, elevation from USGS terrain data.
- Compress distance but keep character: curves, hills, bridges and landmarks stay in real order, closer together.
- Details: green exit signs, mile markers, rumble strips, jersey barriers, medians, overpasses, toll plazas, work zones, state welcome signs.

## State by state
- Florida: flat, straight, long sightlines. Palms, swampy medians, lawyer/fireworks billboards. Afternoon thunderstorms, blinding downpours, heat shimmer; rare hurricane with evacuation traffic.
- Georgia: flat, coastal marshes, big bridges near Savannah/Brunswick, pines. Humid, marsh fog, summer storms.
- South Carolina: flat, endless pines, border-attraction billboard countdown near NC. Fog, storms, coastal hurricane risk.
- North Carolina: gently rolling, pines, military traffic near Fayetteville. Storms, occasional winter ice.
- Virginia: rolling hills begin, Richmond, heavy traffic into Northern Virginia, hidden cops. Fog, first snow chance.
- DC/Maryland: Beltway mess, Fort McHenry Tunnel, rolling terrain. Humid summers, winter ice/snow.
- Delaware: short, flat, toll plaza.
- New Jersey: the Turnpike. Flat, wide, many lanes, refineries, Meadowlands marshes, walls of trucks. Ends at George Washington Bridge into Cross Bronx (near-parking-lot).
- Connecticut/Rhode Island: narrower, curvier, hillier, coastal, many bridges, stop-and-go. Nor'easters.
- Massachusetts/New Hampshire: Boston ring-road chaos, short tolls, rolling hills, denser forest.
- Maine: rolling hills, dense evergreens, empty roads, moose signs, early darkness. Heavy snow, black ice; hardest weather.

## Roadside stops (fictional brands)
Gas stations, rest areas, truck stops, welcome centers, southern all-night diners, Mid-Atlantic convenience stores, northern coffee chains, weigh stations, toll plazas, construction zones, emergency pull-offs.

## Weather
Per-state data tables with odds by season and time of day. Affects grip, visibility, braking, traffic behavior, police presence. Day/night cycle carries over as you drive north.

## Player car
Auto wipers scaling with rain, visible in cockpit/hood view. Windshield drops/streaks, fogged glass, snow at edges, ice in Maine. Auto headlights at dusk/rain/fog/tunnels; high-beam toggle. Road spray, especially trucks. Hydroplaning; reduced grip on wet, far less on snow/black ice. Wind gusts on bridges and coast, worse for tall vehicles. Heat shimmer, sunrise/sunset glare. Class differences: muscle loses traction fastest in rain; truck good in snow but slides on ice.

## NPCs
Wipers and headlights, some forget headlights. Weather behavior: slowing, hazards while moving, pulling onto shoulder, some too fast and crash. More pileups in bad weather; trucks jackknife on ice.

## Police
Fewer pursuits in severe weather, more stops for unsafe driving afterwards.
