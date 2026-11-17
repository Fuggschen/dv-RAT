using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DVRAT
{
    /// <summary>The <see cref="JobView"/> of the job whose ID is in a page variable. Zero or several IDs show no job.</summary>
    public class JobWidget : IPageWidget
    {
        private readonly string _variable;
        private string _pageId;
        private JobView _view;

        public JobWidget(string variable)
        {
            _variable = variable;
        }

        public void Build(RectTransform parent, PageContext context, string pageId, int? rows)
        {
            _pageId = pageId;
            _view = new JobView(parent, rows, context.View(pageId), JobId(context));
        }

        public void Refresh(PageContext context) => _view.Refresh(JobId(context));

        public IEnumerable<SoftKey> SoftKeys(PageContext context) => _view?.SoftKeys() ?? Enumerable.Empty<SoftKey>();

        private string JobId(PageContext context)
        {
            if (context.Tablet == null || string.IsNullOrEmpty(_variable)) return null;
            if (!context.Tablet.Session.Vars(_pageId, null).TryGetValue(_variable, out string value)) return null;
            var ids = Template.Values(value);
            return ids.Count == 1 ? ids.First() : null;
        }
    }
}
