using System.Numerics;

namespace Crawler {
    public class Table {
        // Use a negative width for left alignment
        record Column(string Name, int Index, int Width) {
            public string FmtName => string.Format($"{{0,{Width}}}", Name);
            int RightWidth => Math.Abs(Width);
            int LeftWidth => -RightWidth;
            public string Underline => Fill('-');
            public string Space => Fill(' ');
            public string Fill(char c) => new string(c, RightWidth);

            public string Format(object value) => string.Format($"{{0,{Width}}}", value);
        }
        public Table(params (string Name, int Width)[] columns) {
            foreach (var (name, width) in columns) {
                AddColumn(name, width);
            }
        }
        public void AddRow(params object[] row) {
            var newRow = new List<string>(Columns.Count);
            Rows.Add(newRow);
            foreach (var (index, column) in Columns.Index()) {
                var text = index < row.Length ? column.Format(row[index]) : column.Space;
                newRow.Add(text);
            }
        }
        public void SetColumn(string name, object value, int width = 10) {
            if (!ColumnMap.TryGetValue(name, out var column)) {
                column = Columns[AddColumn(name, width)];
            }
            Rows[^1][column.Index] = column.Format(value);
        }
        public int AddColumn(string name, int width) {
            int index = Columns.Count;
            var column = new Column(name, index, width);
            Columns.Add(column);
            ColumnMap[name] = Columns.Last();
            foreach (var row in Rows) {
                Rows[^1].Add(column.Space);
            }
            return index;
        }
        List<Column> Columns = new();
        Dictionary<string, Column> ColumnMap = new();
        List<List<string>> Rows = new();
        public override string ToString() {
            var result = string.Join(" ", Columns.Select(x => Style.Em.Format(x.FmtName))) + "\n";
            //result += "\n" + string.Join(" ", Columns.Select(x => x.Underline));
            foreach (var row in Rows) {
                result += string.Join(" ", row) + "\n";
            }
            return result;
        }
    }
}
