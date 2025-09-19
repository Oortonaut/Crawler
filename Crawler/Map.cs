using System.Numerics;

namespace Crawler;

public enum TerrainType {
    Flat,
    Rough,
    Broken,
    Shattered,
}

public record Location(
    string Name,
    string Description,
    TerrainType Terrain,
    SimpleEncounterType Type,
    double Wealth,
    float X, float Y) {
    public override string ToString() => $"{Style.Name.Format(Name)} {Type} on {Terrain} Terrain";
    public string Look() {
        return $"{this}\n{Description}\n";
    }
}

public struct Sector(string Name, List<Sector> Neighbors) {
    public List<Location> Locations { get; set; } = new();
}

public class Map {
    public Map(int Height, int Width) {
        Sectors = new Sector[Height, Width];
        double lambda = 3.5;
        foreach (var (X, Y) in Sectors.Index()) {
            var sectorName = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"[Y] + $"{X}";
            var sector = new Sector(sectorName, new List<Sector>(4));
            Sectors[Y, X] = sector;
            var selector = Random.Shared.NextDouble();
            var totalDensity = 0.0;
            int k = 0;
            while (k < 3 * lambda) {
                var nextDensity = totalDensity + CrawlerEx.PoissonPMF(k, lambda);
                if (selector < nextDensity) {
                    break;
                }
                totalDensity = nextDensity;
                ++k;
            }
            // We should always have at least one location
            k = Math.Max(1, k);
            uint seed = (uint)CrawlerEx.HashCombine(Y, X);
            List<Vector2> locations = new();
            List<Vector2> nextLocations = new();
            const float separation = 0.25f;
            const float border = separation / 2;
            const float rborder = 1 - border;
            // 2d halton sequence 2,3
            for (int i = 0; i < k; ++i) {
                float x = (float)CrawlerEx.HaltonSequence(2, seed + (uint)i);
                float y = (float)CrawlerEx.HaltonSequence(5, seed + (uint)i);
                x = x * (1 - border * 2) + border;
                y = y * (1 - border * 2) + border;

                locations.Add(new(x, y));
            }
            foreach (var pos in locations) {
                var loc = new Location(
                    $"Location {pos.X:F2},{pos.Y:F2}",
                    $"A location @{pos.X:F2},{pos.Y:F2}",
                    TerrainType.Flat,
                    SimpleEncounterType.None,
                    0.0,
                    pos.X, pos.Y
                );
                sector.Locations.Add(loc);
                Console.WriteLine(loc.Description);
            }
        }
    }
    public void Generate(double density, Location type) {

    }
    public Sector this[int Y, int X] {
        get {
            return Sectors[Y, X];
        }
    }
    Sector[,] Sectors { get; }
    public string DumpCell(int Y, int X, int Height = 8, int Width = -1) {
        if (Width < 0) {
            Width = Height;
        }
        var sector = Sectors[Y, X];
        var sectorMap = new char[Height, Width];
        for (int i = 0; i < Height; ++i) {
            for (int j = 0; j < Width; ++j) {
                sectorMap[i, j] = '.';
            }
        }
        foreach (var location in sector.Locations) {
            int cx = (int)(CrawlerEx.Frac(location.X) * Width);
            int cy = (int)(CrawlerEx.Frac(location.Y) * Height);
            sectorMap[cy, cx] = 'X';
        }
        string result = "";
        for (int i = 0; i < Height; ++i) {
            for (int j = 0; j < Width; ++j) {
                result += sectorMap[i, j];
            }
            result += '\n';
        }
        return result;
    }
}
