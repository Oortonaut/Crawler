# Crawler

Crawler is An ANSI trading game with some fighting and exploration. 

Upgrade your crawler, a massive train with legs or treads that lets you explore, survive, and even thrive in the harsh environment of Proxima D. 

Inspired by BBS door games, Elite, and Eve Online.

## Author's Note

This is obviously a work in progress, but I'm finally somewhat pleased with it.
This as an experiment in creating a minimal viable product and publishing it.
I didn't quite get the publishing done, but it did start as a minimal viable product.
I accidentally grew attached to it and kept poking and adding.

Lately I've been trying to accelerate things with AI coding using Claude Agent in Rider
and this has improved a lot of things but also pushes some of the cognitive burdens in
the wrong direction. I've let it write and maintain the load/save system, and I just pray
that they work. Vibe coding at its best! And without direct control of the debugger, it's
useless for debugging.

## Future Plans

### Simulation
Only the player's encounter is simulated. Further down the line, I'd like an entire world
simulation. This would involve proper movement for persistent crawlers and a parallel
update of all encounters. 

The economy is completely nonreactive. It's likely that there are locations where the 
player can make infinite money in one sector or location. Improvements there include:
* Supply and demand curves to reduce route profits over time and encourage exploration
* Encounter commodity sources and sinks
* Encounter production chains to drive supply and demand
* Traveling traders to diffuse goods

### Trade
Trade mechanics are almost where I want them with the current economy features.
* [_] Add larger and upgraded segments to the market
* [_] Ongoing tuning

Down the line, I want to add a market view more like Eve Online to make it easier to
trade at large settlements.

### Combat
I'm in the middle of refactoring the whole simulation update from a 1 hour tick
to a 1s tick to try and support more complex combat. Just using a fixed 1s tick
turned out to be, as expected, very slow.
                                         
There is an arena test that can show the current weapon balance.


* [_] Allow targeting of crawler segment areas (Weapons, Traction, Defense, Power)
* [_] Make Aim relevant
* [_] Add creatures eventually for hunting (or being hunted)

### Exploration
Right now the exploration is hot garbage. I mean, the mechanic works OK for resources
but there's not really any sense of discovery. I want to unify the hazards and
resources into a single system that presents a number of different tradeoffs the player
can make, with added unique payoffs like repairs or discovery of unique officers,
segments, or artifacts.

A mining system that lets you trade time mining for successively smaller but randomized
payouts. Pavlov would be proud. 

Way down the line I'd like to add lava tubes and a whole underground level.

### Crawlers
Next phase here is to add utility segments for crawlers and settlements.
* Cockpit  
* Mining, Refining, Manufacturing,  
* Cargo
* Greenhouse
* Security
* Quarters
* Repair
* Sensors
* ECM
