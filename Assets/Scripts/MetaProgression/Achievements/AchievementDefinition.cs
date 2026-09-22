namespace MetaProgression.Achievements
{
    public sealed class AchievementDefinition
    {
        public readonly string id;
        public readonly string displayName;
        public readonly string description;
        public readonly bool hidden;

        public AchievementDefinition(string id, string displayName, string description, bool hidden = false)
        {
            this.id = id;
            this.displayName = displayName;
            this.description = description;
            this.hidden = hidden;
        }
    }
}
