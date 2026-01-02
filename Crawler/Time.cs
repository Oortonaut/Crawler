namespace Crawler;

// Scheduler event wrappers for use with generic Scheduler<TContext, TEvent, TElement, TTime>

public record ActorEvent(IActor Tag, long Time, int Priority = ActorEvent.DefaultPriority): ISchedulerEvent<IActor, long> {
    public const int DefaultPriority = 100;
    public Crawler? AsCrawler => Tag as Crawler;
    public ActorBase? AsActorBase => Tag as ActorBase;
    public virtual void OnStart() { }
    public virtual void OnEnd() { }
    public override string ToString() => $"{Game.TimeString(Time)}.{Priority}: {Tag} {Tag.Location.Description}";
    public record EncounterEvent(IActor Tag, long Time, string Description, Action? Start = null, Action? End = null, int Priority = DefaultPriority): ActorEvent(Tag, Time, Priority) {
        public override string ToString() => base.ToString() + $" \"{Description}\"";
        public override void OnStart() => Start?.Invoke();
        public override void OnEnd() => End?.Invoke();
    }

    public record TravelEvent(IActor Tag, long Time, Location Destination): ActorEvent(Tag, Time, 0) {
        public override string ToString() => base.ToString() + $" to {Destination}";
        public override void OnStart() {
            Tag.Location.GetEncounter().TryRemoveActor(Tag);
        }
        public override void OnEnd() {
            Tag.Location = Destination;
            Destination.GetEncounter().AddActorAt(Tag, Time);
        }
    }
}
