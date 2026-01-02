namespace Crawler;

public partial class Crawler {
    public new record class Init : ActorBase.Init {
        public Roles Role { get; set; }
        public bool InitializeComponents { get; set; } = false;
        public List<Segment> WorkingSegments { get; set; } = new();
    }

    // Data structure for serialization
    public new record class Data : ActorBase.Data {
        // Override Init to use Crawler.Init
        public new Init Init {
            get => (Init)((ActorBase.Data)this).Init;
            set => ((ActorBase.Data)this).Init = value;
        }

        public List<Segment.Data> WorkingSegments { get; set; } = new();
        public int EvilPoints { get; set; }
    }

    public new class Builder : ActorBase.Builder {
        internal Roles _role = Roles.None;
        internal bool _initializeComponents = true;
        internal List<Segment> _workingSegments = new();

        public Builder() : base() { }

        public new Builder WithSeed(ulong seed) {
            base.WithSeed(seed);
            return this;
        }

        public new Builder WithName(string name) {
            base.WithName(name);
            return this;
        }

        public new Builder WithBrief(string brief) {
            base.WithBrief(brief);
            return this;
        }

        public new Builder WithFaction(Factions faction) {
            base.WithFaction(faction);
            return this;
        }

        public new Builder WithLocation(Location location) {
            base.WithLocation(location);
            return this;
        }

        public new Builder WithSupplies(Inventory supplies) {
            base.WithSupplies(supplies);
            return this;
        }

        public new Builder WithCargo(Inventory cargo) {
            base.WithCargo(cargo);
            return this;
        }

        public new Builder AddSupplies(Commodity commodity, float amount) {
            base.AddSupplies(commodity, amount);
            return this;
        }

        public new Builder AddCargo(Commodity commodity, float amount) {
            base.AddCargo(commodity, amount);
            return this;
        }

        public Builder WithRole(Roles role) {
            _role = role;
            return this;
        }

        public Builder WithComponentInitialization(bool initialize) {
            _initializeComponents = initialize;
            return this;
        }

        public Builder AddSegment(Segment segment) {
            _workingSegments.Add(segment);
            return this;
        }

        public Builder WithSegments(IEnumerable<Segment> segments) {
            _workingSegments.AddRange(segments);
            return this;
        }

        public new Init BuildInit() {
            var scheduledInit = base.BuildInit();
            return new Init {
                Seed = scheduledInit.Seed,
                Name = scheduledInit.Name,
                Brief = scheduledInit.Brief,
                Faction = scheduledInit.Faction,
                Location = scheduledInit.Location,
                Supplies = scheduledInit.Supplies,
                Cargo = scheduledInit.Cargo,
                Role = _role,
                InitializeComponents = _initializeComponents,
                WorkingSegments = _workingSegments
            };
        }

        public static Builder Load(Init init) {
            return new Builder()
                .WithSeed(init.Seed)
                .WithName(init.Name)
                .WithBrief(init.Brief)
                .WithFaction(init.Faction)
                .WithLocation(init.Location)
                .WithSupplies(init.Supplies)
                .WithCargo(init.Cargo)
                .WithRole(init.Role)
                .WithComponentInitialization(init.InitializeComponents)
                .LoadWorkingSegments(init.WorkingSegments);
        }

        public Builder LoadWorkingSegments(List<Segment> workingSegments) {
            _workingSegments = workingSegments;
            return this;
        }

        // Getters for constructor access
        internal ulong GetSeed() => _seed;
        internal string GetName() => _name;
        internal string GetBrief() => _brief;
        internal Factions GetFaction() => _faction;
        internal Location GetLocation() => _location;
        internal Inventory GetSupplies() => _supplies;
        internal Inventory GetCargo() => _cargo;
        internal Roles GetRole() => _role;
        internal bool GetInitializeComponents() => _initializeComponents;
        internal List<Segment> GetWorkingSegments() => _workingSegments;

        public Crawler Build() {
            var result = new Crawler(this);
            result.Begin();
            return result;
        }
    }
}
