namespace Crawler;

/// <summary>
/// Calculates context-sensitive priority for AI events.
/// Priority scale: 100 (somewhat) -> 200 (desirable) -> 300 (very) -> 500 (critical) -> 1000 (life/death)
/// </summary>
public static class EventPriority {
    /// <summary>Calculate damage ratio (0 = pristine, 1 = destroyed)</summary>
    public static float DamageRatio(Crawler crawler) {
        float hits = crawler.Segments.Sum(s => s.Hits);
        float maxHits = crawler.Segments.Sum(s => s.MaxHealth);
        return maxHits > 0 ? hits / maxHits : 0;
    }

    /// <summary>Calculate vulnerability score (0-1, higher = more vulnerable)</summary>
    public static float VulnerabilityScore(Crawler crawler) {
        float score = 0;
        if (crawler.IsVulnerable) score += 0.5f;
        if (crawler.IsDisarmed) score += 0.2f;
        if (crawler.IsDefenseless) score += 0.2f;
        if (crawler.IsImmobile) score += 0.1f;
        if (crawler.IsDepowered) score += 0.3f;
        return Math.Clamp(score, 0, 1);
    }

    /// <summary>Calculate relative strength vs another actor (0 = outmatched, 1 = equal, 2+ = dominant)</summary>
    public static float RelativeStrength(Crawler self, IActor target) {
        float selfPower = self.OffenseSegments.OfType<WeaponSegment>().Sum(s => s.Damage) +
                          self.DefenseSegments.OfType<ArmorSegment>().Sum(s => s.Reduction);

        float targetPower = 1f;
        if (target is Crawler tc) {
            targetPower = tc.OffenseSegments.OfType<WeaponSegment>().Sum(s => s.Damage) +
                          tc.DefenseSegments.OfType<ArmorSegment>().Sum(s => s.Reduction);
        }

        return targetPower > 0 ? selfPower / targetPower : 2.0f;
    }

    /// <summary>Calculate normalized cargo value (0-1 scale based on typical cargo range)</summary>
    public static float NormalizedCargoValue(Crawler crawler) {
        float value = crawler.Supplies.ValueAt(crawler.Location) +
                      crawler.Cargo.ValueAt(crawler.Location);
        return Math.Clamp(value / Tuning.EventPriority.CargoValueCap, 0, 1);
    }

    /// <summary>Calculate priority for fleeing (range: 300-1000)</summary>
    public static int ForFlee(Crawler crawler) {
        float damageRatio = DamageRatio(crawler);
        float vulnerability = VulnerabilityScore(crawler);
        float cargoValue = NormalizedCargoValue(crawler);

        // Urgency based on damage, vulnerability, and cargo at stake
        float urgency = damageRatio * Tuning.EventPriority.DamageWeight +
                        vulnerability * Tuning.EventPriority.VulnerabilityWeight +
                        cargoValue * Tuning.EventPriority.CargoValueWeight;

        int priority = (int)(Tuning.EventPriority.FleeBase + urgency * Tuning.EventPriority.FleeScale);
        return Math.Clamp(priority, Tuning.EventPriority.FleeBase, Tuning.EventPriority.SurvivalMax);
    }

    /// <summary>Calculate priority for attacking a target (range: 200-600)</summary>
    public static int ForAttack(Crawler attacker, IActor target) {
        float relativeStrength = RelativeStrength(attacker, target);
        float ownVulnerability = VulnerabilityScore(attacker);

        float urgency;
        if (relativeStrength > 1.5f) {
            // Strong advantage = opportunistic attack (200-300)
            float targetVuln = target is Crawler tc ? VulnerabilityScore(tc) : 0.5f;
            urgency = 0.0f + targetVuln * 0.25f; // Easy target = higher priority
        } else if (relativeStrength > 0.8f) {
            // Equal match = committed (300-400)
            urgency = 0.25f + (relativeStrength - 0.8f) * 0.35f;
        } else {
            // Outmatched but cornered = desperate (400-600)
            urgency = 0.5f + ownVulnerability * 0.5f;
        }

        // Bonus if target has damaged us
        if (attacker.To(target).DamageTaken > 0) {
            urgency = Math.Min(1f, urgency + Tuning.EventPriority.ThreatBonus);
        }

        int priority = (int)(Tuning.EventPriority.CombatBase + urgency * Tuning.EventPriority.CombatScale);
        return Math.Clamp(priority, Tuning.EventPriority.CombatBase, Tuning.EventPriority.CombatMax);
    }

    /// <summary>Calculate priority for extorting a target (range: 200-500)</summary>
    public static int ForExtortion(Crawler bandit, IActor target) {
        float relativeStrength = RelativeStrength(bandit, target);
        float targetCargoValue = target.Supplies.ValueAt(bandit.Location);
        float normalizedValue = Math.Clamp(targetCargoValue / Tuning.EventPriority.CargoValueCap, 0, 1);

        // Higher priority if target has valuable cargo and we're stronger
        float strengthFactor = Math.Clamp(relativeStrength - 0.5f, 0, 1);
        float urgency = normalizedValue * 0.5f + strengthFactor * 0.5f;

        int priority = (int)(Tuning.EventPriority.ExtortionBase + urgency * Tuning.EventPriority.ExtortionScale);
        return Math.Clamp(priority, Tuning.EventPriority.ExtortionBase, Tuning.EventPriority.ExtortionMax);
    }

    /// <summary>Calculate priority for trading (range: 100-300)</summary>
    public static int ForTrade(Crawler trader, float transactionValue) {
        float normalizedValue = Math.Clamp(transactionValue / Tuning.EventPriority.TransactionValueCap, 0, 1);
        float cargoFullness = trader.Cargo.MaxVolume > 0
            ? 1 - (trader.Cargo.AvailableVolume / trader.Cargo.MaxVolume)
            : 0;

        // Higher priority for valuable trades or when cargo is getting full
        float urgency = normalizedValue * 0.6f + cargoFullness * 0.4f;

        int priority = (int)(Tuning.EventPriority.TradeBase + urgency * Tuning.EventPriority.TradeScale);
        return Math.Clamp(priority, Tuning.EventPriority.TradeBase, Tuning.EventPriority.TradeMax);
    }

    /// <summary>Calculate priority for travel (range: 50-150)</summary>
    public static int ForTravel(Crawler crawler, float routeRisk = 0) {
        // Low base priority, increases with route risk
        float urgency = Math.Clamp(routeRisk * 2, 0, 0.5f);

        int priority = (int)(Tuning.EventPriority.TravelBase + urgency * Tuning.EventPriority.TravelScale);
        return Math.Clamp(priority, Tuning.EventPriority.TravelBase, Tuning.EventPriority.TravelMax);
    }

    /// <summary>Calculate priority for convoy coordination (range: 200-500)</summary>
    public static int ForConvoy(Crawler crawler, float routeRisk = 0, float cargoValue = 0) {
        float normalizedCargo = Math.Clamp(cargoValue / Tuning.EventPriority.CargoValueCap, 0, 1);

        // Higher priority if route is dangerous and cargo is valuable
        float urgency = routeRisk * 0.5f + normalizedCargo * 0.5f;

        int priority = (int)(Tuning.EventPriority.ConvoyBase + urgency * Tuning.EventPriority.ConvoyScale);
        return Math.Clamp(priority, Tuning.EventPriority.ConvoyBase, Tuning.EventPriority.ConvoyMax);
    }

    /// <summary>Calculate priority for guard escort duty (range: 300-700)</summary>
    public static int ForGuard(Crawler guard, float threatLevel = 0) {
        // Guards should react quickly to threats
        float urgency = Math.Clamp(threatLevel * 2, 0, 1);

        int priority = (int)(Tuning.EventPriority.GuardBase + urgency * Tuning.EventPriority.GuardScale);
        return Math.Clamp(priority, Tuning.EventPriority.GuardBase, Tuning.EventPriority.GuardMax);
    }

    /// <summary>Calculate priority for guard job-seeking when not hired (range: 100-200)</summary>
    public static int ForJobSeeking(Crawler guard) {
        // Low priority - just looking for work, not urgent
        // Slight increase if guard has been idle longer (approximated by low cargo value)
        float cargoValue = NormalizedCargoValue(guard);
        float urgency = 0.5f - cargoValue * 0.3f; // Less cargo = more need for work

        int priority = (int)(Tuning.EventPriority.TradeBase + urgency * Tuning.EventPriority.TravelScale);
        return Math.Clamp(priority, Tuning.EventPriority.TradeBase, 200);
    }

    /// <summary>Calculate priority for bandit patrol/ambush positioning (range: 100-200)</summary>
    public static int ForBanditPatrol(Crawler bandit) {
        // Low priority when repositioning - extortion and combat take precedence
        float urgency = 0.3f;

        int priority = (int)(Tuning.EventPriority.TradeBase + urgency * Tuning.EventPriority.TravelScale);
        return Math.Clamp(priority, Tuning.EventPriority.TradeBase, 200);
    }

    /// <summary>Calculate priority for customs patrol (range: 100-200)</summary>
    public static int ForCustomsPatrol(Crawler customs) {
        // Low priority patrol movement
        float urgency = 0.3f;

        int priority = (int)(Tuning.EventPriority.TradeBase + urgency * Tuning.EventPriority.TravelScale);
        return Math.Clamp(priority, Tuning.EventPriority.TradeBase, 200);
    }

    /// <summary>Calculate priority for traveler wandering (range: 100-200)</summary>
    public static int ForWander(Crawler traveler) {
        // Low priority casual travel
        float urgency = 0.2f;

        int priority = (int)(Tuning.EventPriority.TradeBase + urgency * Tuning.EventPriority.TravelScale);
        return Math.Clamp(priority, Tuning.EventPriority.TradeBase, 200);
    }
}
