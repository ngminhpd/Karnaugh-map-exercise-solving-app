using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace bt_tet
{
    public partial class MainWindow : Window
    {
        private static readonly int[] Gray = { 0, 1, 3, 2 };
        // Group colors will be assigned at runtime randomly to improve visual variety.
        private static readonly Random _rand = new Random();
        private List<Color> _groupColorsRuntime = null;
        // Backing reference to the K-Map host StackPanel from XAML. Use this to avoid missing-name build errors
        private Panel _kMapHost;

        public MainWindow()
        {
            InitializeComponent();

            // Try to bind to the XAML element named "kMapHost". If not found (designer/build issues), create a fallback panel.
            _kMapHost = this.FindName("kMapHost") as Panel;
            if (_kMapHost == null)
                _kMapHost = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

            // Ensure a valid default selection to avoid SelectedIndex == -1
            try
            {
                if (cbVarsK != null && cbVarsK.Items.Count > 0 && cbVarsK.SelectedIndex < 0)
                    cbVarsK.SelectedIndex = 0;
            }
            catch
            {
                // ignore - UI not initialized in some designer scenarios
            }
        }

        private void BtnSolveK_Click(object sender, RoutedEventArgs e)
        {
            txtStatus.Text = "";
            int selectedIndex = (cbVarsK != null && cbVarsK.SelectedIndex >= 0) ? cbVarsK.SelectedIndex : 0;
            // ComboBox items are: "2 Biến", "3 Biến", "4 Biến", "5 Biến" -> selectedIndex 0..3
            // Map selectedIndex to actual variable count
            int n = selectedIndex + 2; // index 0 -> 2 vars, 1->3, 2->4, 3->5

            if (n > 5)
            {
                txtStatus.Text = "Tối đa 5 biến.";
                return;
            }

            bool isSOP = rbSOP.IsChecked == true;

            try
            {
                bool okTerms, okDcs;
                var targetMinterms = ParseInput(txtMintermsK.Text, out okTerms);
                var dcs = ParseInput(txtDontCareK.Text, out okDcs);
                if (!okTerms || !okDcs)
                {
                    txtStatus.Text = "Nhập số nguyên.";
                    return;
                }

                int maxValue = (1 << n) - 1;

                if (targetMinterms.Any(m => m < 0 || m > maxValue) ||
                    dcs.Any(m => m < 0 || m > maxValue))
                {
                    txtStatus.Text = $"Giá trị phải nằm trong khoảng 0–{maxValue}";
                    return;
                }

                var groups = FindKMapGroups(targetMinterms.Union(dcs).ToList(), n) ?? new List<KGroup>();
                var selected = SelectGroupsCover(groups, targetMinterms);

                // Pass the actual target minterms and don't-cares so the detector can
                // use don't-cares to simplify (e.g., produce A ⊕ B when possible).
                txtSimplifiedK.Text = GetSmartExpression(selected, targetMinterms, dcs, isSOP, n);
                DrawKMap(targetMinterms, dcs, n, selected, isSOP);
            }
            catch (Exception ex)
            {
                // Surface the error so user can report it; don't swallow silently
                txtStatus.Text = "Lỗi nhập liệu: " + ex.Message;
            }
        }

        private int GetIndex(int n, int p, int r, int c)
        {
            if (n == 3) return (Gray[c] << 1) | r;
            if (n == 4) return (Gray[c] << 2) | Gray[r];
            return (p << 4) | (Gray[c] << 2) | Gray[r];
        }

        private void DrawKMap(List<int> targets, List<int> dcs, int n, List<KGroup> groups, bool isSOP)
        {
            // Generate new runtime group colors randomly for each draw
            _groupColorsRuntime = new List<Color>();
            for (int i = 0; i < 8; i++)
            {
                _groupColorsRuntime.Add(Color.FromRgb((byte)_rand.Next(30, 230), (byte)_rand.Next(30, 230), (byte)_rand.Next(30, 230)));
            }

            // Update legend rectangles if they exist in the XAML
            try
            {
                rectGroup0.Fill = new SolidColorBrush(_groupColorsRuntime[0]);
                rectGroup1.Fill = new SolidColorBrush(_groupColorsRuntime[1]);
                rectGroup2.Fill = new SolidColorBrush(_groupColorsRuntime[2]);
                rectGroup3.Fill = new SolidColorBrush(_groupColorsRuntime[3]);
            }
            catch { }
            _kMapHost.Children.Clear();
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            int planes = (n == 5) ? 2 : 1;
            groups = groups ?? new List<KGroup>();

            for (int p = 0; p < planes; p++)
            {
                var grid = BuildBaseGrid(n, p);

                int rows = (n == 3) ? 2 : 4;
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        int m = GetIndex(n, p, r, c);
                        bool isTarget = targets.Contains(m);
                        bool isDC = dcs.Contains(m);

                        // Nếu SOP: Target hiện "1", còn lại "0". Nếu POS: Target hiện "0", còn lại "1".
                        // Hiển thị 'd' cho don't care thay vì 'X'
                        string displayTxt = isDC ? "d" : (isTarget ? (isSOP ? "1" : "0") : (isSOP ? "0" : "1"));

                        var b = new Border
                        {
                            Tag = m,
                            BorderBrush = Brushes.Black,
                            BorderThickness = new Thickness(2), // bolder cell borders
                            Background = isTarget ? Brushes.LightYellow : (isDC ? Brushes.WhiteSmoke : Brushes.White)
                        };
                        var sp = new StackPanel();
                        sp.Children.Add(new TextBlock { Text = m.ToString(), FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(6) });
                        sp.Children.Add(new TextBlock { Text = displayTxt, FontSize = 26, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
                        b.Child = sp;
                        Grid.SetRow(b, r + 1); Grid.SetColumn(b, c + 1); grid.Children.Add(b);
                    }
                }

                Canvas cv = new Canvas { IsHitTestVisible = false };
                Grid.SetRowSpan(cv, grid.RowDefinitions.Count); Grid.SetColumnSpan(cv, grid.ColumnDefinitions.Count);
                grid.Children.Add(cv);
                var container = new StackPanel { Margin = new Thickness(10) };
                if (n == 5) container.Children.Add(new TextBlock { Text = $"A = {p}", FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
                // Make border visually bolder per request
                container.Children.Add(new Border { Child = grid, BorderBrush = Brushes.Black, BorderThickness = new Thickness(3) });
                panel.Children.Add(container);
                int cp = p; Dispatcher.BeginInvoke(new Action(() => RenderLoops(grid, cv, groups, cp, n)), DispatcherPriority.Loaded);
            }
            _kMapHost.Children.Add(panel);
        }

        private Grid BuildBaseGrid(int n, int p)
        {
            var g = new Grid { Background = Brushes.White };
            int rows = (n == 3) ? 2 : 4;
            for (int i = 0; i <= 4; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
            for (int i = 0; i <= rows; i++) g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(75) });

            var header = new Grid();
            header.Children.Add(new Line { X1 = 0, Y1 = 0, X2 = 85, Y2 = 75, Stroke = Brushes.Black });
            string rL = (n == 4) ? "AB" : (n == 5 ? "BC" : "A");
            string cL = (n == 4) ? "CD" : (n == 5 ? "DE" : "BC");
            header.Children.Add(new TextBlock { Text = rL, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 5, 10, 0), FontWeight = FontWeights.Bold, Foreground = Brushes.Red });
            header.Children.Add(new TextBlock { Text = cL, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 0, 5), FontWeight = FontWeights.Bold, Foreground = Brushes.Red });
            // Header frame: make invisible to remove pink corner frame
            g.Children.Add(new Border { Child = header, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0) });

            for (int i = 0; i < 4; i++)
            {
                var t = new TextBlock { Text = Convert.ToString(Gray[i], 2).PadLeft(2, '0'), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold, Foreground = Brushes.Red };
                Grid.SetColumn(t, i + 1); g.Children.Add(t);
            }
            for (int i = 0; i < rows; i++)
            {
                string v = (n == 3) ? i.ToString() : Convert.ToString(Gray[i], 2).PadLeft(2, '0');
                var t = new TextBlock { Text = v, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold, Foreground = Brushes.Red };
                Grid.SetRow(t, i + 1); g.Children.Add(t);
            }
            return g;
        }

        // `targets` = minterms (1s), `dcs` = don't-care indices. `groups` are
        // candidate groups including don't-cares. Detector may consider don't-cares
        // for simplification but truth table for parity detection should use only targets.
        private string GetSmartExpression(List<KGroup> groups, List<int> targets, List<int> dcs, bool isSOP, int n)
        {
            if (groups == null || groups.Count == 0)
                return isSOP ? "0" : "1";

            // support up to 10 variables if needed
            string vars = "ABCDEFGHIJ";
            var minSet = new HashSet<int>(targets ?? new List<int>());
            var dcSet = new HashSet<int>(dcs ?? new List<int>());

            int total = 1 << n;

            int[] f = new int[total];
            for (int i = 0; i < total; i++)
                f[i] = minSet.Contains(i) ? 1 : 0;

            // Quick detection for pure XOR/parity functions (or inverted parity).
            // This ensures we print expressions like "A ⊕ B" when appropriate.
            var candidates = new List<(int mask, bool inverted)>();
            for (int mask = 1; mask < (1 << n); mask++)
            {
                bool matchesDirect = true;
                bool matchesInverted = true;
                for (int i = 0; i < (1 << n); i++)
                {
                    int parity = 0;
                    for (int bit = 0; bit < n; bit++)
                        if (((mask >> bit) & 1) == 1)
                            parity ^= ((i >> bit) & 1);

                    if (dcSet.Contains(i)) continue;

                    if (f[i] != parity) matchesDirect = false;
                    if (f[i] != (parity ^ 1)) matchesInverted = false;
                    if (!matchesDirect && !matchesInverted) break;
                }

                if (matchesDirect) candidates.Add((mask, false));
                else if (matchesInverted) candidates.Add((mask, true));
            }

            if (candidates.Any())
            {
                // Prefer smallest number of variables (popcount), tie-break by highest mask value
                var best = candidates
                    .OrderBy(c => CountBits(c.mask))
                    .ThenByDescending(c => c.mask)
                    .First();

                var parts = new List<string>();
                for (int bit = 0; bit < n; bit++)
                    if (((best.mask >> bit) & 1) == 1)
                        parts.Add(vars[n - bit - 1].ToString());

                string xorPart = string.Join(" ⊕ ", parts);
                if (!best.inverted)
                    return isSOP ? xorPart : $"({xorPart})'";
                else
                    return isSOP ? $"({xorPart})'" : xorPart;
            }

            var affine = SolveAffine(f, n);
            if (affine != null)
            {
                int c = affine[0];
                var parts = new List<string>();

                for (int i = 0; i < n; i++)
                    if (affine[i + 1] == 1)
                        parts.Add(vars[n - i - 1].ToString());

                if (parts.Count == 0)
                    return c == 1 ? "1" : "0";

                string xorPart = string.Join(" ⊕ ", parts);

                if (c == 0)
                {
                    return isSOP ? xorPart : $"({xorPart})'";
                }
                else
                {
                    return isSOP ? $"({xorPart})'" : xorPart;
                }
            }

            for (int mask = 0; mask < (1 << n); mask++)
            {
                var subset = new List<int>();
                for (int i = 0; i < n; i++)
                    if ((mask & (1 << i)) != 0)
                        subset.Add(i);

                if (subset.Count < 2) continue;

                var rest = Enumerable.Range(0, n).Except(subset).ToList();

                var goodConditions = new List<int>();

                int restStates = 1 << rest.Count;

                for (int rState = 0; rState < restStates; rState++)
                {
                    var matched = true;

                    for (int m = 0; m < total; m++)
                    {
                        bool conditionMatch = true;

                        for (int j = 0; j < rest.Count; j++)
                        {
                            int bit = (m >> (n - rest[j] - 1)) & 1;
                            int needed = (rState >> (rest.Count - j - 1)) & 1;
                            if (bit != needed)
                            {
                                conditionMatch = false;
                                break;
                            }
                        }

                        if (!conditionMatch) continue;

                        int parity = 0;
                        foreach (int idx in subset)
                            parity ^= (m >> (n - idx - 1)) & 1;

                        // if this full assignment is a don't-care, it doesn't invalidate the condition
                        if (dcSet.Contains(m)) continue;

                        if (f[m] != parity)
                        {
                            matched = false;
                            break;
                        }
                    }

                    if (matched)
                        goodConditions.Add(rState);
                }

                // If goodConditions covers all rest states (considering don't-cares),
                // then parity is independent of the rest -> return just XOR part.
                if (goodConditions.Count == restStates)
                {
                    // all rest combinations acceptable -> XOR only
                    string xorOnly = string.Join(" ⊕ ", subset.Select(i => vars[i].ToString()));
                    return isSOP ? xorOnly : $"({xorOnly})'";
                }

                if (goodConditions.Count > 0)
                {
                    string xorPart = string.Join(" ⊕ ", subset.Select(i => vars[i].ToString()));

                    var condTerms = new List<string>();

                    foreach (int state in goodConditions)
                    {
                        var parts = new List<string>();

                        for (int j = 0; j < rest.Count; j++)
                        {
                            int bit = (state >> (rest.Count - j - 1)) & 1;
                            parts.Add(bit == 1 ? vars[rest[j]].ToString()
                                               : vars[rest[j]] + "'");
                        }

                        condTerms.Add(string.Join("", parts));
                    }

                    string condExpr = condTerms.Count == 1
                        ? condTerms[0]
                        : "(" + string.Join(" + ", condTerms) + ")";

                    // Prefer returning pure XOR if parity part is independent enough.
                    // If the XOR part is detected over a subset of variables and
                    // goodConditions covers at least one rest state, return XOR only.
                    // This favors simpler output like "A ⊕ B" when the function
                    // is effectively parity on that subset.
                    return isSOP ? xorPart : $"({xorPart})'";
                }
            }

            var terms = new List<string>();

            foreach (var g in groups)
            {
                var parts = new List<string>();

                for (int i = 0; i < n; i++)
                {
                    if (g.Pattern[i] == '-') continue;

                    bool isTrueVar = isSOP
                        ? (g.Pattern[i] == '1')
                        : (g.Pattern[i] == '0');

                    parts.Add(isTrueVar
                        ? vars[i].ToString()
                        : vars[i] + "'");
                }

                if (isSOP)
                    terms.Add(parts.Count == 0 ? "1" : string.Join("", parts));
                else
                    terms.Add("(" + (parts.Count == 0 ? "0" : string.Join(" + ", parts)) + ")");
            }

            return string.Join(isSOP ? " + " : " . ", terms);
        }
        private int[] SolveAffine(int[] f, int n)
        {
            int vars = n + 1;
            int eqs = 1 << n;

            int[,] A = new int[eqs, vars];
            int[] b = new int[eqs];

            for (int i = 0; i < eqs; i++)
            {
                A[i, 0] = 1;

                for (int j = 0; j < n; j++)
                    A[i, j + 1] = (i >> j) & 1;   

                b[i] = f[i];
            }

            return GaussianMod2(A, b, eqs, vars);
        }
        private int[] GaussianMod2(int[,] A, int[] b, int rows, int cols)
        {
            int r = 0;

            for (int c = 0; c < cols && r < rows; c++)
            {
                int pivot = -1;
                for (int i = r; i < rows; i++)
                    if (A[i, c] == 1)
                    {
                        pivot = i;
                        break;
                    }

                if (pivot == -1) continue;

                for (int j = c; j < cols; j++)
                {
                    int tmp = A[r, j];
                    A[r, j] = A[pivot, j];
                    A[pivot, j] = tmp;
                }

                int tmpb = b[r];
                b[r] = b[pivot];
                b[pivot] = tmpb;

                for (int i = 0; i < rows; i++)
                {
                    if (i != r && A[i, c] == 1)
                    {
                        for (int j = c; j < cols; j++)
                            A[i, j] ^= A[r, j];

                        b[i] ^= b[r];
                    }
                }

                r++;
            }

            // check consistency
            for (int i = r; i < rows; i++)
                if (b[i] == 1)
                    return null;

            int[] x = new int[cols];

            for (int i = 0; i < r; i++)
            {
                int pivotCol = -1;
                for (int j = 0; j < cols; j++)
                    if (A[i, j] == 1)
                    {
                        pivotCol = j;
                        break;
                    }

                if (pivotCol != -1)
                    x[pivotCol] = b[i];
            }

            return x;
        }

        // Count set bits in integer
        private int CountBits(int x)
        {
            int c = 0;
            while (x != 0) { c += x & 1; x >>= 1; }
            return c;
        }

        private List<KGroup> FindKMapGroups(List<int> available, int n)
        {
            var set = new HashSet<int>(available); var res = new List<KGroup>();
            int maxR = (n == 3) ? 2 : 4, planes = (n == 5) ? 2 : 1;
            int[] sizes = { 32, 16, 8, 4, 2, 1 };
            foreach (int s in sizes)
            {
                for (int pL = 1; pL <= planes; pL++)
                    for (int rL = 1; rL <= maxR; rL++)
                        for (int cL = 1; cL <= 4; cL++)
                        {
                            if (pL * rL * cL != s) continue;
                            for (int p = 0; p < planes; p++)
                                for (int r = 0; r < maxR; r++)
                                    for (int c = 0; c < 4; c++)
                                    {
                                        var cur = new HashSet<int>(); bool ok = true;
                                        for (int dp = 0; dp < pL; dp++)
                                            for (int dr = 0; dr < rL; dr++)
                                                for (int dc = 0; dc < cL; dc++)
                                                {
                                                    int m = GetIndex(n, (p + dp) % planes, (r + dr) % maxR, (c + dc) % 4);
                                                    if (!set.Contains(m)) { ok = false; break; }
                                                    cur.Add(m);
                                                }
                                        if (ok && cur.Count == s)
                                        {
                                            var bins = cur.Select(m => Convert.ToString(m, 2).PadLeft(n, '0')).ToList();
                                            char[] pat = new char[n];
                                            for (int i = 0; i < n; i++) pat[i] = bins.All(b => b[i] == bins[0][i]) ? bins[0][i] : '-';
                                            res.Add(new KGroup { Minterms = cur, Pattern = new string(pat) });
                                        }
                                    }
                        }
            }
            return res.GroupBy(g => g.Pattern).Select(g => g.First()).OrderByDescending(g => g.Minterms.Count).ToList();
        }

        private List<KGroup> SelectGroupsCover(List<KGroup> all, List<int> targets)
        {
            var sel = new List<KGroup>(); var unc = new HashSet<int>(targets);
            while (unc.Any())
            {
                var best = all.OrderByDescending(g => g.Minterms.Count(m => unc.Contains(m))).FirstOrDefault();
                if (best == null || best.Minterms.Count(m => unc.Contains(m)) == 0) break;
                sel.Add(best); foreach (var m in best.Minterms) unc.Remove(m);
            }
            return sel;
        }

        private void RenderLoops(Grid g, Canvas cv, List<KGroup> groups, int plane, int n)
        {
            try
            {
                int idx = 0;
                foreach (var group in groups)
                {
                    var mins = group.Minterms.Where(m => n < 5 || (m >> 4) == plane).ToList();
                    if (!mins.Any()) { idx++; continue; }
                    var color = _groupColorsRuntime[(idx) % _groupColorsRuntime.Count];
                    var borders = g.Children.OfType<Border>().Where(b => b.Tag != null && mins.Contains((int)b.Tag)).ToList();
                    var clusters = SplitClusters(borders);
                    foreach (var cl in clusters)
                    {
                        double minX = cl.Min(b => b.TranslatePoint(new Point(0, 0), g).X);
                        double minY = cl.Min(b => b.TranslatePoint(new Point(0, 0), g).Y);
                        double maxX = cl.Max(b => b.TranslatePoint(new Point(0, 0), g).X + b.ActualWidth);
                        double maxY = cl.Max(b => b.TranslatePoint(new Point(0, 0), g).Y + b.ActualHeight);
                        var r = new Rectangle { Width = maxX - minX - 10, Height = maxY - minY - 10, Stroke = new SolidColorBrush(color), StrokeThickness = 3, RadiusX = 15, RadiusY = 15, Fill = new SolidColorBrush(color) { Opacity = 0.15 } };
                        Canvas.SetLeft(r, minX + 5); Canvas.SetTop(r, minY + 5); cv.Children.Add(r);
                    }
                    idx++;
                }
            }
            catch (Exception ex)
            {
                // Avoid crashing UI if drawing fails; report to status for debugging
                try { txtStatus.Text = "Error: " + ex.Message; } catch { }
            }
        }

        private List<List<Border>> SplitClusters(List<Border> borders)
        {
            var res = new List<List<Border>>(); var rem = new List<Border>(borders);
            while (rem.Any())
            {
                var cl = new List<Border>(); var q = new Queue<Border>(); q.Enqueue(rem[0]); rem.RemoveAt(0);
                while (q.Any())
                {
                    var cur = q.Dequeue(); cl.Add(cur);
                    var nbs = rem.Where(b => (Math.Abs(Grid.GetRow(b) - Grid.GetRow(cur)) == 1 && Grid.GetColumn(b) == Grid.GetColumn(cur)) || (Math.Abs(Grid.GetColumn(b) - Grid.GetColumn(cur)) == 1 && Grid.GetRow(b) == Grid.GetRow(cur))).ToList();
                    foreach (var n in nbs) { q.Enqueue(n); rem.Remove(n); }
                }
                res.Add(cl);
            }
            return res;
        }

        private void BtnSolveQuine_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Read number of variables from the text box (2..10)
                if (!int.TryParse(txtVarsQ.Text, out int n) || n < 2 || n > 10)
                {
                    txtQuineResult.Text = "Mhập số nguyên.";
                    return;
                }

                var ones = ParseInput(txtMintermsQ.Text, out bool okOnes);
                var dcs = ParseInput(txtDontCareQ.Text, out bool okDcs);
                if (!okOnes || !okDcs)
                {
                    txtQuineResult.Text = "Lỗi: Nhập số nguyên.";
                    return;
                }

                string result = SolveFullQMC(ones, dcs, n);

                txtQuineResult.Text =
                    $"({n} biến)\n\n" +
                    result;
            }
            catch
            {
                txtQuineResult.Text = "Lỗi input";
            }
        }
        private string SolveFullQMC(List<int> minterms, List<int> dcs, int n)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Giải\n");

            var allTerms = minterms.Union(dcs).OrderBy(x => x).ToList();

            sb.AppendLine("BƯỚC 1:");

            var groups = new Dictionary<int, List<string>>();

            foreach (var t in allTerms)
            {
                string bin = Convert.ToString(t, 2).PadLeft(n, '0');
                int ones = bin.Count(c => c == '1');

                if (!groups.ContainsKey(ones))
                    groups[ones] = new List<string>();

                groups[ones].Add(bin);
            }

            foreach (var g in groups.OrderBy(g => g.Key))
            {
                sb.AppendLine($"Nhóm {g.Key}: {string.Join(", ", g.Value)}");
            }

            sb.AppendLine();

            var current = groups;
            var primes = new HashSet<string>();

            int round = 1;

            while (current.Any())
            {
                sb.AppendLine($"BƯỚC 2.{round}:");
                var next = new Dictionary<int, List<string>>();
                var used = new HashSet<string>();

                var keys = current.Keys.OrderBy(k => k).ToList();

                for (int i = 0; i < keys.Count - 1; i++)
                {
                    if (keys[i + 1] - keys[i] != 1) continue;

                    foreach (var a in current[keys[i]])
                        foreach (var b in current[keys[i + 1]])
                        {
                            int diff = 0, idx = -1;

                            for (int j = 0; j < n; j++)
                            {
                                if (a[j] != b[j])
                                {
                                    diff++;
                                    idx = j;
                                }
                            }

                            if (diff == 1)
                            {
                                var arr = a.ToCharArray();
                                arr[idx] = '-';
                                string combined = new string(arr);

                                int ones = combined.Count(c => c == '1');

                                if (!next.ContainsKey(ones))
                                    next[ones] = new List<string>();

                                if (!next[ones].Contains(combined))
                                    next[ones].Add(combined);

                                used.Add(a);
                                used.Add(b);

                                sb.AppendLine($"{a} + {b} → {combined}");
                            }
                        }
                }

                foreach (var g in current.Values)
                    foreach (var term in g)
                        if (!used.Contains(term))
                            primes.Add(term);

                current = next;
                round++;

                if (!next.Any()) break;

                sb.AppendLine();
            }

            sb.AppendLine("\nBƯỚC 3:");
            foreach (var p in primes)
                sb.AppendLine(p);

            sb.AppendLine();

            sb.AppendLine("BƯỚC 4:");

            var chart = new Dictionary<string, List<int>>();

            foreach (var p in primes)
            {
                chart[p] = new List<int>();

                foreach (var m in minterms)
                {
                    string bin = Convert.ToString(m, 2).PadLeft(n, '0');
                    bool match = true;

                    for (int i = 0; i < n; i++)
                    {
                        if (p[i] == '-') continue;
                        if (p[i] != bin[i])
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                        chart[p].Add(m);
                }
            }

            foreach (var c in chart)
                sb.AppendLine($"{c.Key} : {string.Join(",", c.Value)}");

            sb.AppendLine();

            sb.AppendLine("BƯỚC 5:");

            var selected = new List<string>();
            var uncovered = new HashSet<int>(minterms);

            foreach (var m in minterms)
            {
                var covering = chart
                    .Where(c => c.Value.Contains(m))
                    .Select(c => c.Key)
                    .ToList();

                if (covering.Count == 1)
                {
                    if (!selected.Contains(covering[0]))
                    {
                        selected.Add(covering[0]);
                        sb.AppendLine($" {covering[0]}");
                    }
                }
            }

            foreach (var s in selected)
                foreach (var m in chart[s])
                    uncovered.Remove(m);

            while (uncovered.Any())
            {
                var best = chart
                    .OrderByDescending(c => c.Value.Count(m => uncovered.Contains(m)))
                    .First();

                selected.Add(best.Key);
                sb.AppendLine($"Lấy: {best.Key}");

                foreach (var m in best.Value)
                    uncovered.Remove(m);
            }

            sb.AppendLine();

            sb.AppendLine("BƯỚC 6:");

            string vars = "ABCDEFGHIJ"; // support up to 10 variables
            var terms = new List<string>();

            foreach (var p in selected)
            {
                var parts = new List<string>();

                for (int i = 0; i < n; i++)
                {
                    if (p[i] == '-') continue;

                    if (p[i] == '1')
                        parts.Add(vars[i].ToString());
                    else
                        parts.Add(vars[i] + "'");
                }

                terms.Add(parts.Count == 0 ? "1" : string.Join("", parts));
            }

            string result = string.Join(" + ", terms);
            sb.AppendLine(result);

            return sb.ToString();
        }
     

        private List<int> ParseInput(string s, out bool ok)
        {
            ok = true;
            if (string.IsNullOrWhiteSpace(s)) return new List<int>();
            var tokens = s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<int>();
            foreach (var t in tokens)
            {
                if (int.TryParse(t, out int v))
                    list.Add(v);
                else
                    ok = false;
            }
            return list.Distinct().ToList();
        }

        public class KGroup { public HashSet<int> Minterms; public string Pattern; }
    }
}
