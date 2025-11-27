namespace Crawler;

public partial class Crawler {
    public class Builder {
        internal ulong _seed;
        internal string _name = "";
        internal string _brief = "";
        internal Factions _faction = Factions.Independent;
        internal Location _location = null!;
        internal Inventory _supplies = new();
        internal Inventory _cargo = new();
        internal Roles _role = Roles.None;
        internal bool _initializeComponents = true;

        public Builder() { }

        public Builder WithSeed(ulong seed) {
            _seed = seed;
            return this;
        }

        public Builder WithName(string name) {
            _name = name;
            return this;
        }

        public Builder WithBrief(string brief) {
            _brief = brief;
            return this;
        }

        public Builder WithFaction(Factions faction) {
            _faction = faction;
            return this;
        }

        public Builder WithLocation(Location location) {
            _location = location;
            return this;
        }

        public Builder WithSupplies(Inventory supplies) {
            _supplies = supplies;
            return this;
        }

        public Builder WithCargo(Inventory cargo) {
            _cargo = cargo;
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

        public Crawler Build() {
            // Generate name from seed if not provided
            if (string.IsNullOrEmpty(_name)) {
                _name = Names.HumanName(_seed);
            }

            var crawler = new Crawler(this);

            // Initialize components based on role if requested
            if (_initializeComponents && _role != Roles.None) {
                var rng = new XorShift(_seed);
                crawler.InitializeComponents(rng.Seed());
            }

            return crawler;
        }
    }
}
