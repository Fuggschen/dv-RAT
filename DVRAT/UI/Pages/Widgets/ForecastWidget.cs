using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DVRAT
{
    /// <summary>The game's forecast, one row per slot. The current slot is highlighted, earlier ones dimmed.</summary>
    public class ForecastWidget : IPageWidget
    {
        private const float TimeWidth = 130f;
        private const float IconSize = 28f;
        private const float RowHeight = 30f;
        private const float TemperatureWidth = 80f;

        private PagedList _slots;

        public void Build(RectTransform parent, PageContext context, string pageId, int? rows)
        {
            _slots = new PagedList(parent, rows ?? 0, "The game has no forecast yet; it makes one at 06:00.", RowHeight,
                context.View(pageId), "forecast");
        }

        public void Refresh(PageContext context)
        {
            var world = context.Tablet?.CurrentWorldData;
            var slots = world?.Forecast ?? new List<ForecastSlot>();
            int now = ForecastSlot.IndexAt(slots, world?.GameTime);
            _slots?.SetRows(slots.Select((slot, i) => (slot, i)), (cells, entry) => BuildRow(cells, entry.slot, entry.i, now));
        }

        private static void BuildRow(IRowCells cells, ForecastSlot slot, int index, int now)
        {
            bool current = index == now;
            var color = current ? Theme.Highlight : now >= 0 && index < now ? Theme.TextDim : Theme.Text;
            var style = current ? FontStyle.Bold : FontStyle.Normal;
            cells.Cell($"{slot.Start:HH:mm} - {slot.End:HH:mm}", TimeWidth, color, style);
            // The column stays when the icon is missing, so the words line up.
            cells.Icon(WeatherIcons.For(slot.Icon), IconSize);
            cells.Cell(ForecastSlot.Describe(slot.Icon), 0, color, style);
            if (slot.TemperatureC.HasValue)
                cells.Label(Format.Temperature(slot.TemperatureC.Value), TemperatureWidth, size: Theme.FontSmall, color: color,
                    anchor: TextAnchor.MiddleRight, style: style);
        }

        public IEnumerable<SoftKey> SoftKeys(PageContext context) => _slots?.SoftKeys() ?? Enumerable.Empty<SoftKey>();
    }
}
