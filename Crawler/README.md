# Crawler

Crawler is An ANSI trading game with some fighting and exploration. 

Upgrade your crawler, a massive train with legs or treads that lets you explore, survive, and even thrive in the harsh environment of Proxima D. 

Inspired by BBS door games, Elite, and Eve Online.

Currently optimizing living world simulation. Object Oriented C# isn't the best for performance. A medium-sized world (#1) has 256 crawlers and 22 settlements. I want to run it for hundreds of years, but it's only running 200x on my machine. I think the end result will be some sort of encounter-based scheduling (again; I had this before and removed it as too complicated) alongside an actual ECS. 

## Author's Note

This is obviously a work in progress, but I'm finally somewhat pleased with it.
This as an experiment in creating a minimal viable product and publishing it.
I didn't quite get the publishing done, but it did start as a minimal viable product.
I accidentally grew attached to it and kept poking and adding.

Using Claude Code now. Big improvement in so many ways.
<prior>Lately I've been trying to accelerate things with AI coding using Claude Agent in Rider
and this has improved a lot of things but also pushes some of the cognitive burdens in
the wrong direction. I've let it write and maintain the load/save system, and I just pray
that they work. Vibe coding at its best! And without direct control of the debugger, it's
useless for debugging.

## Future Plans

### Simulation
...a parallel update of all encounters. Yup this is really slow when it's serial. 

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

## Logging Rubric
                                                                          
### Formatting
Searchability

### Severity
The log severity covers three different functions: error reporting, monitoring events, and monitoring state periodically. There's a little functional overlap between log levels since we're using a scalar severity.

State changes, initialization, etc. are a level more severe than state logging. Debug events are a level lower than normal operation events; this is sometimes hard to differentiate if you aren't explicit about guarding your debug code with `#ifdef` or `[Conditional("Debug")]` or the like. 
                                            
A "frequent" event happens more than once per frame per player.

### Errors
* Fatal/Critical: something went very wrong, and it's unsafe to continue. Use for out of memory, heap corruption, etc. Log and flush, then exit.
* Error: Something went wrong, but you can safely shut down. Use for code assertions, data structure corruption with a limited extent, critical assets missing, etc. Contain the damage and allow the user to save if needed/possible, then shut down.
* Warning: Something went wrong, but you can safely continue. Use for most recoverable errors, like missing files, bad configuration, network timeouts, etc. Try to recover by using defaults or fallbacks.

### Logging
* Display/Information: infrequent major events, no periodic state logging. Use to monitor the operation of systems without being overwhelming. "What's going on in the scheduler?"
* Log/Trace: periodic major state, frequent minor events, infrequent major debug events and state. Use to monitor detailed system operations. "What's getting dispatched?" 
* Verbose/Debug: frequent minor debug events. 
* VeryVerbose/Debug: very frequent or marginally important debug events, detailed debug state.
