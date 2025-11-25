# TODO
                  
## Big Ol' Bugs

* [ ] Wait function needs to wait for any messages and then reschedule the player
* [ ] Ultimatum Data - just use object? or make more typesafe. 
* [ ] Add ActorToActor.StateValues { Hostile, Wary, Neutral, Friendly } instead of Hostile flag
## Missing Features

## Wish List
                                                                      
* [ ] Move Menus and UI to component
* [ ] Move segments out of inventory
* [ ] Quests
* [ ] Faction Standing
* [ ] Improved save/load
* [ ] Market views
* [ ] Better WeakReference - struct Weak<T>, forward object identity for utility
* [ ] Precalculate and cache more of the filtered segments lists (like ReactorSegments etc.)
* Documentation improvements
  * [ ] Add a players guide
  * [ ] Reduce amount of code in documentation - we assume that the reader will have access to source, so refer to particular methods for example usages, etc. The docs should have conceptual information and info that's hard to localize to a specific class or method. 
  * [ ] Organize code and methods inside classes into regions
  * [ ] XML documentation comments
  * [ ] Prettify comments 
                                            
* [ ] Add component flags 
  * [ ] Thinking - keep a separate list of thinking components for speed.
* [ ] Encounter scheduling just keep the scheduled time on the ActorScheduled
## Finished

* [x] Use deterministic seeding like I should have from the start (2025-11-08)

* [x] Make a separate combat mode - improved scheduling
* [x] Change to 1-5 minute scheduling -- hourly max delay
* [x] Refactor generate fire and weapon segments to support fire rate.
* [x] Improve and show prohibition states
* [x] Need a --seed command line option
* [x] Time not flowing properly outside of combat
* [x] Improve the suffocation mechanic
* [x] Change Passengers and Soldiers to a mid-game raw material and a late-game refined material
* [x] Dynamic crawlers don't appear to be following the right seed - sometimes it will spawn with one in seed=1, sometimes one will appear after a few hours, and sometimes you starve first.
* [x] dynamic crawlers are off
* [x] Self-repair
