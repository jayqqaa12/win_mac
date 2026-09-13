using System.Globalization;

namespace WinMac.Core.Services;

/// <summary>一条股票/指数的实时行情（价格取自接口的 ASCII 数字字段，名称来自内置对照表）。</summary>
public sealed record StockQuote(
    string Code,
    string DisplayName,
    double Price,
    double PrevClose,
    double Change,
    double ChangePct,
    DateTime LastUpdate);

/// <summary>
/// A股/指数实时行情服务：通过腾讯公共行情接口 qt.gtimg.cn 免 key 获取。
/// 只解析响应中的 ASCII 数字字段（现价/昨收），据此自行计算涨跌与涨跌幅，
/// 因此无需处理响应体里的 GBK 中文编码；展示名取自内置对照表。
/// </summary>
public sealed class StockQuoteService
{
    private static readonly HttpClient Http = new();

    /// <summary>默认自选：指数 + 蓝筹个股（顺序即卡片展示顺序）。</summary>
    public static IReadOnlyList<(string Code, string Name)> DefaultWatchlist { get; } =
        new (string, string)[]
        {
            ("sh000001", "上证指数"),
            ("sz399001", "深证成指"),
            ("sz399006", "创业板指"),
            ("sh600036", "招商银行"),
            ("sz300750", "宁德时代"),
            ("sh600519", "贵州茅台"),
        };

    private static readonly Dictionary<string, string> NameByCode =
        DefaultWatchlist.ToDictionary(w => w.Code, w => w.Name);

    /// <summary>拉取自选列表实时行情；失败时返回空列表（调用方保留原数据）。</summary>
    public async Task<List<StockQuote>> GetQuotesAsync()
    {
        var url = "https://qt.gtimg.cn/q=" + string.Join(",", DefaultWatchlist.Select(w => w.Code));
        try
        {
            string text = await Http.GetStringAsync(url).ConfigureAwait(false);
            var result = new List<StockQuote>();
            var now = DateTime.Now;

            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string code = line[..eq].Trim().Replace("v_", "").Trim('"', ' ');
                string inside = line[(eq + 1)..].Trim().Trim('"', ';', ' ');
                var f = inside.Split('~');
                if (f.Length < 5) continue;
                if (!TryNum(f[3], out double price) || !TryNum(f[4], out double prev)) continue;

                double change = price - prev;
                double pct = prev > 0 ? change / prev * 100 : 0;
                string name = NameByCode.TryGetValue(code, out var nm) ? nm : code;
                result.Add(new StockQuote(code, name, price, prev, change, pct, now));
            }
            return result;
        }
        catch
        {
            // 网络异常/解析失败 → 返回空，由 UI 保留上一次数据。
            return new List<StockQuote>();
        }
    }

    private static bool TryNum(string s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}