using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Crawler;

public class ActorToActor {
    public bool WasHostile => DamageCreated > 0;
    public bool WasDamaged => DamageTaken > 0; // Track if we've taken any damage from them
    public bool Hostile = false; // Are we hostile
    public bool Surrendered = false;
    public int DamageCreated = 0;
    public int DamageInflicted = 0;
    public int DamageTaken = 0;
}

public class Crawler: IActor {
    public static Crawler NewRandom(Location here, float LocWealthScale, float CommoditiesFraction, EArray<SegmentKind, float> segmentClassWeights, Faction faction = Faction.Player) {
        var newInv = new Inventory();
        float Budget = here.Wealth * LocWealthScale;
        float CrewSplit = CommoditiesFraction * 0.5f;
        float GoodsSplit = CommoditiesFraction * 0.5f;
        newInv.AddRandomInventory(here, Budget, CrewSplit, GoodsSplit, true, segmentClassWeights, faction);
        return new Crawler(here, newInv);
    }
    public Crawler(Location location, Inventory inventory) {
        Inv = inventory;
        Location = location;
        Name = Names.HumanName();
        _markup = Math.Max(CrawlerEx.NextGaussian(Tuning.Trade.rate, Tuning.Trade.sd), 1);
    }
    public string Name { get; set; }
    public string Brief(IActor viewer) {
        var C = CrawlerDisplay(viewer).Split('\n');

        C[1] += $" Power: {TotalCharge:F1} + {TotalGeneration:F1}/t  Drain: Off:{OffenseDrain:F1}  Def:{DefenseDrain:F1}  Move:{MovementDrain:F1}";
        C[2] += $" Weight: {Mass:F0} / {Lift:F0}T, {Speed:F0}km/h";
        if (this == viewer) {
            float RationsPerDay = CrewInv * Tuning.Crawler.RationsPerCrewDay;
            C[3] += $" Cash: {ScrapInv:F1}¢¢  Fuel: {FuelInv:F1}";
            C[4] += $" Crew: {CrewInv:F0}  Rations: {RationsInv:F1}, {RationsPerDay:F1}/d  Morale: {MoraleInv}";
            C[5] += $" Fuel: {FuelPerHr:F1}/d, {MovementFuelUsePerKm*100:F2}/100km";
        } else {
            C = C.Take(3).ToArray();
        }
        return string.Join("\n", C);
    }
    public EActorFlags Flags { get; set; } = EActorFlags.Mobile;
    public Location Location { get; set; }

    static Crawler() {
        //Faction.Bandit.To(Faction.Player).SetMenuFunc(BanditMenu);
        //Faction.Bandit.To(Faction.Player).DefaultCrawlerRelation = banditToPlayer;

    }
    public float FuelPerHr => StandbyDrain / FuelEfficiency;
    public float MovementFuelUsePerKm => Tuning.Crawler.FuelPerKm * MovementDrain / FuelEfficiency;
    public float WagesPerHr => CrewInv * Tuning.Crawler.WagesPerCrewDay / 24;
    public float RationsPerHr => CrewInv * Tuning.Crawler.RationsPerCrewDay / 24;
    public (float Fuel, float Time) FuelTimeTo(Location location) {
        float dist = Location.Distance(location);
        float startSpeed = Speed;
        float endSpeed = SpeedOn(location.Terrain);
        float terrainRate = Math.Min(startSpeed, endSpeed);
        if (terrainRate <= 0) {
            return (-1, -1);
        }
        float time = dist / terrainRate;
        float fuel = MovementFuelUsePerKm * dist + FuelPerHr * time;
        return (fuel, time);
    }

    public Faction Faction { get; set; }
    public int EvilPoints { get; set; } = 0;
    public List<IProposal> StoredProposals { get; private set; } = new();
    public virtual IEnumerable<IProposal> Proposals() => StoredProposals;
    //IEnumerable<MenuItem> BanditActions(Crawler other, int attackIndex) {
    //    var banditToPlayer = GetRelationTo(other);
    //    if (!IsDestroyed) {
    //        yield return new ActionMenuItem($"A{attackIndex}", $"Attack {Name}", _ => other.Attack(this));
    //        if (IsVulnerable && !banditToPlayer.Surrendered) {
    //
    //            //yield return new TradeMenuItem(AcceptSurrenderOffer.MakeSurrenderTo(other, this), attackIndex);
    //        }
    //    }
    //}

    public bool Pinned() {
        bool CantEscape(Crawler crawler) => crawler.To(this).Hostile && Speed < crawler.Speed;
        return Location.Encounter.Actors.Any(a => a is Crawler crawler && CantEscape(crawler));
    }

    public void Tick() {
        UpdateSegments();
        if (Game.Instance.TimeSeconds % 3600 == 0) {
            Recharge(1);
            if (RationsInv <= 0) {
                Message("You are out of rations and your crew is starving.");
                CrewInv *= 0.99f;
                RationsInv = 0;
            }
            if (IsDepowered) {
                Message("Your oxygen is running low.");
                MoraleInv -= 1.0f;
            }
        }
        if (CrewInv == 0) {
            // TODO: List killers
            if (RationsInv == 0) {
                End(EEndState.Starved, "All the crew have starved.");
            } else {
                End(EEndState.Killed, "The crew have been killed.");
            }
        }
        if (MoraleInv == 0) {
            End(EEndState.Revolt, "The crew has revolted.");
        }
        if (!UndestroyedSegments.Any()) {
            End(EEndState.Destroyed, "Your crawler has been utterly destroyed.");
        }
        UpdateSegments();
    }
    public void Tick(IEnumerable<IActor> Actors) {
        if (Faction is Faction.Bandit) {
            TickBandit(Actors);
        }
        UpdateSegments();
    }
    protected void TickBandit(IEnumerable<IActor> Actors) {
        if (IsDepowered) {
            Message($"{Name} is radio silent.");
            return;
        }
        foreach (var actor in Actors) {
            var relation = To(actor);
            if (actor.Faction == Faction.Player) {
                if (relation.Hostile) {
                    this.Attack(actor);
                }
                break;
            }
        }
    }

    public void Message(string message) {
        // TODO: Message history for other actors
        if (this == Game.Instance.Player) {
            CrawlerEx.Message(message);
        }
    }
    public bool Knows(Location loc) => _locations.ContainsKey(loc);

    Inventory _inventory = new Inventory();
    public Inventory Inv {
        get => _inventory;
        set {
            _inventory = value;
            UpdateSegments();
        }
    }
    public Inventory TradeInv { get; } = new Inventory();
    public void UpdateSegments() {
        _allSegments = Inv.Segments.OrderBy(s => (s.ClassCode, s.Cost)).ToList();

        _activeSegments.Clear();
        _disabledSegments.Clear();
        _destroyedSegments.Clear();
        _segmentsByClass.Initialize(() => new List<Segment>());
        _activeSegmentsByClass.Initialize(() => new List<Segment>());

        foreach (var segment in _allSegments) {
            _segmentsByClass[segment.SegmentKind].Add(segment);
            switch (segment.State) {
                case Segment.Working.Active:
                    _activeSegmentsByClass[segment.SegmentKind].Add(segment);
                    _activeSegments.Add(segment);
                    _undestroyedSegments.Add(segment);
                    break;
                case Segment.Working.Disabled:
                    _disabledSegments.Add(segment);
                    _undestroyedSegments.Add(segment);
                    break;
                case Segment.Working.Destroyed:
                    _destroyedSegments.Add(segment);
                    break;
                case Segment.Working.Deactivated:
                    _undestroyedSegments.Add(segment);
                    break;
                case Segment.Working.Packaged:
                    _undestroyedSegments.Add(segment);
                    break;
            }
        }
    }
    List<Segment> _allSegments = [];
    List<Segment> _activeSegments = [];
    List<Segment> _disabledSegments = [];
    List<Segment> _destroyedSegments = [];
    List<Segment> _undestroyedSegments = [];
    EArray<SegmentKind, List<Segment>> _segmentsByClass = new();
    EArray<SegmentKind, List<Segment>> _activeSegmentsByClass = new();
    public List<Segment> SegmentsFor(SegmentKind segmentKind) => _segmentsByClass[segmentKind];
    public List<Segment> ActiveSegmentsFor(SegmentKind segmentKind) => _activeSegmentsByClass[segmentKind];
    public List<Segment> Segments => _allSegments;
    public List<Segment> ActiveSegments => _activeSegments;
    public List<Segment> DisabledSegments => _disabledSegments;
    public List<Segment> DestroyedSegments => _destroyedSegments;
    public List<Segment> UndestroyedSegments => _undestroyedSegments;
    public IEnumerable<TractionSegment> TractionSegments => _activeSegmentsByClass[SegmentKind.Traction].Cast<TractionSegment>();
    public IEnumerable<OffenseSegment> OffenseSegments => _activeSegmentsByClass[SegmentKind.Offense].Cast<OffenseSegment>();
    public IEnumerable<OffenseSegment> WeaponSegments => _activeSegmentsByClass[SegmentKind.Offense].OfType<WeaponSegment>();
    public IEnumerable<DefenseSegment> DefenseSegments => _activeSegmentsByClass[SegmentKind.Defense].Cast<DefenseSegment>();
    public IEnumerable<DefenseSegment> CoreDefenseSegments => _activeSegmentsByClass[SegmentKind.Defense].Cast<DefenseSegment>();
    public IEnumerable<PowerSegment> PowerSegments => _activeSegmentsByClass[SegmentKind.Power].Cast<PowerSegment>();

    public float Speed => SpeedOn(Location.Terrain);
    public float Lift => LiftOn(Location.Terrain);
    public float MovementDrain => MovementDrainOn(Location.Terrain);
    public float OffenseDrain => OffenseSegments.Sum(s => s.Drain);
    public float DefenseDrain => DefenseSegments.Sum(s => s.Drain);
    public float TotalDrain => ActiveSegments.Sum(s => s.Drain);
    public float StandbyDrain => TotalDrain * Tuning.Crawler.StandbyFraction;

    public float SpeedOn(TerrainType t) {
        return EvaluateMove(t).Speed;
    }
    public float LiftOn(TerrainType t) => TractionSegments.Sum(ts => ts.LiftOn(t));
    public float MovementDrainOn(TerrainType t) {
        return EvaluateMove(t).Drain;
    }
    public record struct Move(float Speed, float Drain, string? Error);
    public Move EvaluateMove(TerrainType t) {
        float bestSpeed = 0;
        float drain = 0;
        string bestMsg = "";
        float generation = TotalGeneration;
        float weight = Mass;
        var segments = TractionSegments.ToArray();
        for (TerrainType i = TerrainType.Flat; i <= t; ++i) {
            float speed = segments.Sum(ts => ts.SpeedOn(i));
            float drainSum = segments.Sum(ts => ts.DrainOn(i));
            float generationLimit = Math.Min(1, generation / drainSum);
            float liftFraction = weight / segments.Sum(ts => ts.LiftOn(t));
            List<string> notes = new();
            if (generationLimit < 1) {
                notes.Add("low gen");
            }
            speed *= generationLimit;
            if (liftFraction > 1) {
                liftFraction = ( float ) Math.Pow(liftFraction, 0.6f);
                notes.Add("too heavy");
                drain *= liftFraction;
                speed /= liftFraction;
            }
            if (speed > bestSpeed) {
                bestSpeed = speed;
                drain = drainSum;
                bestMsg = notes.Any() ? "("+string.Join(", ", notes)+")" : "";
            }
        }
        return new Move(bestSpeed, drain, bestMsg == "" ? null : bestMsg);
    }

    public float Mass => Inv.Mass;
    public float TotalCharge => PowerSegments.Any() ? PowerSegments.Select(s => s switch {
            ReactorSegment rs => rs.Charge,
            _ => 0,
        })
        .Sum() : 0;
    public float TotalGeneration =>
        PowerSegments.OfType<ReactorSegment>().Sum(s => s.Generation) +
        PowerSegments.OfType<ChargerSegment>().Sum(s => s.Generation);

    public float FuelEfficiency => 0.4f;
    public string Report() {
        // Header with crawler name and location
        string result = Style.Name.Format($"[{Name}]");
        result += $" Evil: {EvilPoints}\n";

        // Add inventory summary
        result += "\nInventory:\n";
        var commodityTable = new Table(
            ("Commodity", -16),
            ("Amount", 10),
            ("Mass", 8),
            ("Volume", 8),
            ("Local Value", 12)
        );

        foreach (var (commodity, amount) in Inv.Commodities.Select((amt, idx) => (( Commodity ) idx, amt)).Where(pair => pair.amt > 0)) {
            var value = amount * commodity.Value(Location);
            commodityTable.AddRow(
                commodity.ToString(),
                commodity == Commodity.Scrap ? $"{amount:F1}¢¢" :
                commodity is Commodity.Crew or Commodity.Soldiers or Commodity.Passengers ? $"{( int ) amount}" :
                $"{amount:F1}",
                $"{commodity.Mass():F3}",
                $"{commodity.Volume():F3}",
                $"{value:F1}¢¢"
            );
        }
        result += commodityTable.ToString() + "\n";

        // Group segments by kind
        var segmentsByKind = Segments
            .GroupBy(s => s.SegmentKind)
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var group in segmentsByKind) {

            switch (group.Key) {
                case SegmentKind.Power:
                    var powerTable = new Table(
                        ("Name", -16),
                        ("State", -12),
                        ("Health", 6),
                        ("Weight", 6),
                        ("Drain", 6),
                        ("Cap", 6),
                        ("Gen", 6)
                    );
                    foreach (var segment in group) {
                        string cap = "", gen = "";
                        if (segment is ReactorSegment rs) {
                            cap = $"{rs.Capacity:F1}";
                            gen = $"{rs.Generation:F1}";
                        } else if (segment is ChargerSegment cs) {
                            gen = $"{cs.Generation:F1}";
                        }
                        powerTable.AddRow(
                            segment.Name,
                            segment.StatusLine(Location),
                            $"{segment.Health}/{segment.MaxHits}",
                            $"{segment.Weight:F1}",
                            $"{segment.Drain:F1}",
                            cap,
                            gen
                        );
                    }
                    result += powerTable.ToString();
                    break;

                case SegmentKind.Traction:
                    var tractionTable = new Table(
                        ("Name", -16),
                        ("State", -12),
                        ("Health", 6),
                        ("Weight", 6),
                        ("Drain", 6),
                        ("Lift", 6),
                        ("Speed", 6),
                        ("Terrain", -10)
                    );
                    foreach (var segment in group.Cast<TractionSegment>()) {
                        tractionTable.AddRow(
                            segment.Name,
                            segment.StatusLine(Location),
                            $"{segment.Health}/{segment.MaxHits}",
                            $"{segment.Weight:F1}",
                            $"{segment.Drain:F1}",
                            $"{segment.Lift:F1}",
                            $"{segment.Speed:F1}",
                            segment.TerrainLimit.ToString()
                        );
                    }
                    result += tractionTable.ToString();
                    break;

                case SegmentKind.Offense:
                    var offenseTable = new Table(
                        ("Name", -16),
                        ("State", -12),
                        ("Health", 6),
                        ("Weight", 6),
                        ("Drain", 6),
                        ("Dmg", 6),
                        ("Rate", 6),
                        ("Shots", 6),
                        ("Aim", 6)
                    );
                    foreach (var segment in group) {
                        string dmg = "", rate = "", shots = "", aim = "";
                        if (segment is WeaponSegment ws) {
                            dmg = $"{ws.Damage:F1}";
                            rate = $"{ws.Rate:F1}";
                            shots = $"{ws.Shots:F1}";
                            aim = $"{ws.Aim:F1}";
                        }
                        offenseTable.AddRow(
                            segment.Name,
                            segment.StatusLine(Location),
                            $"{segment.Health}/{segment.MaxHits}",
                            $"{segment.Weight:F1}",
                            $"{segment.Drain:F1}",
                            dmg,
                            rate,
                            shots,
                            aim
                        );
                    }
                    result += offenseTable.ToString();
                    break;

                case SegmentKind.Defense:
                    var defenseTable = new Table(
                        ("Name", -16),
                        ("State", -12),
                        ("Health", 6),
                        ("Weight", 6),
                        ("Drain", 6),
                        ("Cost", 6),
                        ("Reduction", -12),
                        ("Charge", -12)
                    );
                    foreach (var segment in group) {
                        string reduction = "";
                        string charge = "";
                        if (segment is ArmorSegment ars) {
                            reduction = $"{ars.Reduction}";
                        } else if (segment is PlatingSegment ps) {
                            reduction = $"{ps.Mitigation:P0}";
                        } else if (segment is ShieldSegment ss) {
                            reduction = $"{ss.ShieldLeft}";
                            charge = $"+{ss.Charge}/t";
                        }
                        defenseTable.AddRow(
                            segment.Name,
                            segment.StatusLine(Location),
                            $"{segment.Health}/{segment.MaxHits}",
                            $"{segment.Weight:F1}",
                            $"{segment.Drain:F1}",
                            $"{segment.Cost:F1}",
                            reduction,
                            charge
                        );
                    }
                    result += defenseTable.ToString();
                    break;
            }
        }

        return result;
    }
    public string StateString() {
        var Adjs = new List<string>();
        if (!string.IsNullOrWhiteSpace(EndMessage)) {
            Adjs.Add(Style.SegmentDestroyed.Format(EndMessage));
        }
        if (IsDepowered) {
            Adjs.Add("Depowered");
        }
        if (IsImmobile && (Flags & EActorFlags.Mobile) != 0) {
            Adjs.Add("Immobile");
        }
        if (IsDisarmed) {
            Adjs.Add("Disarmed");
        }
        if (IsDefenseless) {
            Adjs.Add("Defenseless");
        }
        if (Adjs.Any()) {
            return " (" + string.Join(", ", Adjs) + ")";
        } else {
            return "";
        }
    }
    public string StateString(IActor viewer) {
        var Adjs = new List<string>();
        var relation = To(viewer);
        if (relation.Hostile) {
            Adjs.Add("Hostile");
        }
        if (relation.Surrendered) {
            Adjs.Add("Surrendered");
        }
        var result = StateString();
        if (Adjs.Any()) {
            result += $" (" + string.Join(", ", Adjs) + ")";
        }
        return result;
    }
    public string CrawlerDisplay(IActor viewer) {
        IEnumerable<StyledString> coloredSegments = [
            new StyledString(Style.SegmentNone, "|||||"),
            .. Segments.Select(s => s.Colored(Location)),
            new StyledString(Style.SegmentNone, "|||||"),
        ];
        string result = coloredSegments.TransposeJoinStyled();
        var Adjs = StateString(viewer);
        result = Style.Name.Format(Name) + $": {Faction}{Adjs}\n{result}";
        return result;
    }

    public float ScrapInv {
        get => Inv[Commodity.Scrap];
        set => Inv[Commodity.Scrap] = value;
    }
    public float FuelInv {
        get => Inv[Commodity.Fuel];
        set => Inv[Commodity.Fuel] = value;
    }
    public float RationsInv {
        get => Inv[Commodity.Rations];
        set => Inv[Commodity.Rations] = value;
    }
    public float CrewInv {
        get => Inv[Commodity.Crew];
        set => Inv[Commodity.Crew] = value;
    }
    public float MoraleInv {
        get => Inv[Commodity.Morale];
        set => Inv[Commodity.Morale] = value;
    }
    public void End(EEndState state, string message = "") {
        EndMessage = $"{state}: {message}";
        EndState = state;
        Message($"Game Over: {message} ({state})");
    }
    public string EndMessage { get; set; } = string.Empty;
    public EEndState? EndState { get; set; }
    public bool IsDepowered => !PowerSegments.Any() || FuelInv <= 0;
    public bool IsImmobile => EvaluateMove(Location.Terrain).Speed == 0;
    public bool IsDisarmed => !OffenseSegments.Any() || IsDepowered;
    public bool IsDefenseless => !DefenseSegments.Any();

    public bool IsDestroyed => EndState is not null;
    public bool IsVulnerable => IsDefenseless || IsImmobile || IsDisarmed || IsDepowered;

    public string About => ToString() + StateString();
    public override string ToString() => $"{Name} ({Faction})";

    public List<HitRecord> CreateFire() {
        List<HitRecord> fire = new();
        float availablePower = TotalCharge;
        var offense = OffenseSegments.GroupBy(s => s is WeaponSegment).ToDictionary(s => s.Key, s => s.ToList());
        var weapons = offense.GetValueOrDefault(true) ?? new();
        var selectedWeapons = new List<OffenseSegment>();
        var nonWeapons = offense.GetValueOrDefault(false) ?? new();
        var selectedNonWeapons = new List<OffenseSegment>();
        weapons.Shuffle();
        foreach (var segment in weapons) {
            if (availablePower >= segment.Drain) {
                selectedWeapons.Add(segment);
                availablePower -= segment.Drain;
            }
        }
        foreach (var segment in nonWeapons) {
            if (availablePower >= segment.Drain) {
                selectedNonWeapons.Add(segment);
                availablePower -= segment.Drain;
            }
        }
        var used = TotalCharge - availablePower;
        DrawPower(used);

        foreach (WeaponSegment segment in selectedWeapons.OfType<WeaponSegment>()) {
            fire.AddRange(segment.GenerateFire(0));
        }
        return fire;
    }
    public void Recharge(int Count) {
        for (int i = 0; i < Count; ++i) {
            Segments.Do(s => s.Tick());
            float overflowPower = PowerSegments.OfType<ReactorSegment>().Sum(s => s.Generate());
            overflowPower += PowerSegments.OfType<ChargerSegment>().Sum(s => s.Generate());
            FeedPower(overflowPower);
            ScrapInv -= WagesPerHr;
            RationsInv -= RationsPerHr;
            FuelInv -= FuelPerHr;
        }
    }
    // Returns excess (wasted) power
    float FeedPower(float delta) {
        if (delta <= 0) {
            return 0;
        }

        var reactorSegments = PowerSegments.OfType<ReactorSegment>();
        // Distributes the power to segments based on their charge available
        var segmentCapAvail = reactorSegments.Select(s => s.Capacity - s.Charge);
        var totalCapAvail = segmentCapAvail.Sum();
        delta = Math.Clamp(delta, 0, totalCapAvail);
        float result = 0;
        if (delta > totalCapAvail) {
            result = delta - totalCapAvail;
            delta = totalCapAvail;
        }
        if (delta <= 0) {
            return result;
        }
        // Distributes the power to segments based on their charge available
        // ( this is the same as the above, but with a different order of operations)
        reactorSegments.Zip(segmentCapAvail, (rs, capacity) => rs.Charge += delta * (capacity / totalCapAvail)).Do();
        return 0;
    }
    float DrawPower(float delta) {
        if (delta <= 0) {
            return 0;
        }

        var reactorSegments = PowerSegments.OfType<ReactorSegment>();
        // Distributes the power to segments based on their charge available
        var segmentCharge = reactorSegments.Select(s => s.Charge);
        var totalCharge = segmentCharge.Sum();
        delta = Math.Clamp(delta, 0, totalCharge);
        float result = 0;
        if (delta > totalCharge) {
            result = delta - totalCharge;
            delta = totalCharge;
        }
        if (delta <= 0) {
            return result;
        }
        // Distributes the power to segments based on their charge available
        // ( this is the same as the above, but with a different order of operations)
        reactorSegments.Zip(segmentCharge, (rs, charge) => rs.Charge -= delta * (charge / totalCharge)).Do();
        return 0;
    }
    public void ReceiveFire(IActor from, List<HitRecord> fire) {
        if (!Inv.Segments.Any() || !fire.Any()) {
            return;
        }
        
        bool wasDestroyed = IsDestroyed;
        int totalDamageDealt = 0;

        var relation = To(from);
        var wasAlreadyDamaged = relation.WasDamaged;

        relation.Hostile = true;

        string msg = "";
        foreach (var hit in fire) {
            int damage = hit.Damage.StochasticInt();
            int originalDamage = damage;
            var hitType = hit.Hit;
            var phase0Segments = CoreDefenseSegments.OfType<ShieldSegment>().Where(s => s.ShieldLeft > 0).ToList();
            var phase1Segments = CoreDefenseSegments.Except(phase0Segments).ToList();
            var phase2Segments = Segments.Except(CoreDefenseSegments).ToList();
            if (hitType is HitType.Misses) {
                damage = 0;
            }
            msg += $"{hit.Weapon.Name} {hitType}";
            if (damage > 0) {
                var shieldSegment = phase0Segments.ChooseRandom();
                if (shieldSegment != null) {
                    var (remaining, armorMsg) = shieldSegment.AddDmg(hitType, damage);
                    damage = remaining;
                    msg += armorMsg;
                }
            }
            if (damage > 0) {
                var armorSegment = phase1Segments.ChooseRandom();
                if (armorSegment != null) {
                    var (remaining, armorMsg) = armorSegment.AddDmg(hitType, damage);
                    damage = remaining;
                    msg += armorMsg;
                }
            }
            if (damage > 0) {
                var hitSegment = phase2Segments.ChooseRandom();
                if (hitSegment != null) {
                    var (rem, hitMsg) = hitSegment.AddDmg(hitType, damage);
                    msg += hitMsg;
                    damage = rem;
                }
            }
            if (damage > 0) {
                // TODO: Also maybe lose commodities here
                var (rem, crewMsg) = LoseCrew(damage);
                msg += crewMsg;
                damage = rem;
            }

            // Track total damage actually dealt (original minus what's left over)
            int actualDamageDealt = originalDamage - damage;
            totalDamageDealt += actualDamageDealt;

            msg += ".\n";
        }

        // Update damage tracking in relation
        relation.DamageTaken += totalDamageDealt;

        // Apply first damage morale penalty only if this is the first time taking damage from this attacker
        if (!wasAlreadyDamaged && totalDamageDealt > 0) {
            float dMorale = Tuning.Crawler.MoraleTakeAttack;
            Inv[Commodity.Morale] += dMorale;
            msg += $"{dMorale} morale for taking fire.\n";
        }

        // Check if this crawler was destroyed and give attacker morale bonus
        if (!wasDestroyed && IsDestroyed) {
            if (relation.Hostile) {
                float dMorale = Tuning.Crawler.MoraleHostileDestroyed;
                from.Inv[Commodity.Morale] += dMorale;
                Message($"{dMorale} morale for destroying {Name}");
            } else {
                float dHostileMorale = Tuning.Crawler.MoraleHostileDestroyed;
                float dMorale = Tuning.Crawler.MoraleFriendlyDestroyed;
                from.Inv[Commodity.Morale] += dMorale;
                if (from is Crawler fromCrawler) {
                    float evilage = fromCrawler.EvilPoints / Tuning.EvilLimit;
                    evilage = Math.Clamp(evilage, 0.0f, 1.0f);
                    dMorale = CrawlerEx.Lerp(dMorale, dHostileMorale, evilage);
                }
                Message($"{dMorale} morale for destroying friendly {Name}");
            }
        }
        Message(msg.TrimEnd());
        UpdateSegments();
    }

    (int, string) LoseCrew(int damage) {
        var start = (int)CrewInv;
        CrewInv -= damage;
        var taken = start - (int)CrewInv;
        var startMorale = MoraleInv;
        MoraleInv -= Tuning.Crawler.MoraleAdjCrewLoss * taken;
        var moraleLoss = startMorale - MoraleInv;
        var remaining = damage - taken;
        return (remaining, $" killing {CrewInv} crew and {moraleLoss} morale");
    }

    Dictionary<IActor, ActorToActor> _relations = new();
    Dictionary<Location, ActorLocation> _locations = new();

    public bool Knows(IActor other) => _relations.ContainsKey(other);
    public ActorToActor To(IActor other) {
        return _relations.GetOrAddNewValue(other, () => NewRelation(other));
    }
    ActorToActor NewRelation(IActor to) {
        var result = new ActorToActor();
        bool isTradeSettlement = Location.Type is EncounterType.Settlement && Location.Encounter.Faction is Faction.Trade;
        if (Faction is Faction.Bandit && to.Faction is Faction.Player && !isTradeSettlement) {
            // Bandits check player evilness before turning hostile
            if (to is Crawler playerCrawler) {
                float evilness = playerCrawler.EvilPoints;
                if (evilness >= Tuning.Trade.banditHostilityThreshold) {
                    // Higher evilness = higher chance of hostility
                    float hostilityChance = Tuning.Trade.banditHostilityChance * (evilness / Tuning.Trade.banditHostilityThreshold);
                    hostilityChance = Math.Clamp(hostilityChance, 0.0f, 1.0f);

                    if (Random.Shared.NextDouble() < hostilityChance) {
                        result.Hostile = true;
                    }
                } else {
                    // Low evilness - bandits might trade instead of fighting
                    result.Hostile = false;
                }
            } else {
                // Default behavior for non-crawler players
                result.Hostile = true;
            }
        }
        return result;
    }
    public ActorLocation To(Location location) {
        return _locations.GetOrAddNewValue(location);
    }

    // Accessor methods for save/load
    public Dictionary<IActor, ActorToActor> GetRelations() => _relations;
    public bool RelatedTo(IActor other) => _relations.ContainsKey(other);
    public int Embark(Location location) {
        throw new NotImplementedException();
    }
    public int Domes { get; init; } = 0;
    float _markup = 1.0f;
    public float Markup => _markup * (Faction is Faction.Bandit ? Tuning.Trade.banditRate : Tuning.Trade.rate);
}
