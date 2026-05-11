using System.Collections.Generic;

namespace EventSystem
{
    /// <summary>
    /// 単一のイベント定義（パース後）。id は名前から自動生成。
    /// </summary>
    public class EventDefinition
    {
        public string id;
        public string name;
        public string flavor;
        public EventCondition condition = new EventCondition();
        public List<EventChoice> choices = new List<EventChoice>();
    }
}
