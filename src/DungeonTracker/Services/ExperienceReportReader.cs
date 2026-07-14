using VoK.Sdk.Ddo;
using VoK.Sdk.Enums;
using VoK.Sdk.Properties;

namespace DungeonTracker.Services;

public static class ExperienceReportReader
{
    private static readonly uint SummaryTitleId = (uint)DdoProperty.Dungeon_Summary_Title;
    private static readonly uint SummaryTextId = (uint)DdoProperty.Dungeon_Summary_Text;
    private static readonly uint UiDifficultyId = (uint)DdoProperty.Dungeon_UI_DifficultyName;
    private static readonly uint ChallengeScoreId = (uint)DdoProperty.Dungeon_Challenge_Score;

    public static ExperienceReportSnapshot Read(IDdoGameDataProvider provider, ExperienceReportCache? cache = null)
    {
        var character = provider.GetCurrentCharacter()?.PropertyCollection;
        var title = PropertyTextReader.Read(provider, character, SummaryTitleId);
        var text = PropertyTextReader.Read(provider, character, SummaryTextId);
        var source = string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(text) ? string.Empty : "character";

        if (string.IsNullOrWhiteSpace(title))
        {
            title = PropertyTextReader.ReadFromCollection(provider, provider.GetServerProperties(), SummaryTitleId);
            if (!string.IsNullOrWhiteSpace(title))
                source = "server";
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            text = PropertyTextReader.ReadFromCollection(provider, provider.GetServerProperties(), SummaryTextId);
            if (!string.IsNullOrWhiteSpace(text))
                source = "server";
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            title = PropertyTextReader.ReadFromCollection(provider, provider.GetSubscriptionProperties(), SummaryTitleId);
            if (!string.IsNullOrWhiteSpace(title))
                source = "subscription";
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            text = PropertyTextReader.ReadFromCollection(provider, provider.GetSubscriptionProperties(), SummaryTextId);
            if (!string.IsNullOrWhiteSpace(text))
                source = "subscription";
        }

        var uiDifficultyPropertyId = ReadUiDifficultyPropertyId(character);
        if (uiDifficultyPropertyId == null)
            uiDifficultyPropertyId = ReadUiDifficultyFromCollection(provider.GetServerProperties());

        if (uiDifficultyPropertyId == null)
            uiDifficultyPropertyId = ReadUiDifficultyFromCollection(provider.GetSubscriptionProperties());

        var challengeScore = ReadChallengeScore(character);
        if (challengeScore == null)
            challengeScore = ReadChallengeScoreFromCollection(provider.GetServerProperties());

        var uiDifficulty = uiDifficultyPropertyId is uint propertyId
            ? QuestDifficultyResolver.MapPropertyId(propertyId)
            : null;

        var snapshot = new ExperienceReportSnapshot
        {
            Title = title,
            Text = text,
            UiDifficultyLabel = uiDifficulty,
            UiDifficultyPropertyId = uiDifficultyPropertyId,
            ChallengeScore = challengeScore,
            Source = source,
            Parsed = ExperienceReportParser.Parse(title, text, uiDifficulty),
            LooksComplete = ExperienceReportParser.Parse(title, text, uiDifficulty).LooksComplete
                || InstanceSummaryReader.LooksLikeCompletionSummary(title, text)
        };

        if (cache != null)
        {
            cache.Merge(snapshot);
            var cached = cache.ToSnapshot();
            if (cached.IsVisible && !snapshot.IsVisible)
                return cached;

            if (cached.IsVisible && snapshot.IsVisible)
            {
                return new ExperienceReportSnapshot
                {
                    Title = string.IsNullOrWhiteSpace(snapshot.Title) ? cached.Title : snapshot.Title,
                    Text = string.IsNullOrWhiteSpace(snapshot.Text) ? cached.Text : snapshot.Text,
                    UiDifficultyLabel = snapshot.UiDifficultyLabel ?? cached.UiDifficultyLabel,
                    UiDifficultyPropertyId = snapshot.UiDifficultyPropertyId ?? cached.UiDifficultyPropertyId,
                    ChallengeScore = snapshot.ChallengeScore ?? cached.ChallengeScore,
                    Source = string.IsNullOrWhiteSpace(snapshot.Source) ? cached.Source : $"{snapshot.Source}+{cached.Source}",
                    Parsed = ExperienceReportParser.Parse(
                        string.IsNullOrWhiteSpace(snapshot.Title) ? cached.Title : snapshot.Title,
                        string.IsNullOrWhiteSpace(snapshot.Text) ? cached.Text : snapshot.Text,
                        snapshot.UiDifficultyLabel ?? cached.UiDifficultyLabel),
                    LooksComplete = snapshot.LooksComplete || cached.LooksComplete
                };
            }
        }

        return snapshot;
    }

    private static uint? ReadUiDifficultyPropertyId(IPropertyCollection? properties)
    {
        if (properties == null)
            return null;

        try
        {
            var raw = properties.GetUInt32PropertyValue(UiDifficultyId);
            if (raw is uint value && value != 0)
                return value;
        }
        catch
        {
            // Ignore read failures.
        }

        return null;
    }

    private static uint? ReadUiDifficultyFromCollection(IReadOnlyCollection<IProperty>? properties)
    {
        if (properties == null)
            return null;

        foreach (var property in properties)
        {
            if (property.PropertyId != UiDifficultyId)
                continue;

            return PortalDifficultyReader.TryReadPropertyId(property);
        }

        return null;
    }

    private static int? ReadChallengeScore(IPropertyCollection? properties)
    {
        if (properties == null)
            return null;

        try
        {
            return properties.GetInt32PropertyValue(ChallengeScoreId);
        }
        catch
        {
            return null;
        }
    }

    private static int? ReadChallengeScoreFromCollection(IReadOnlyCollection<IProperty>? properties)
    {
        if (properties == null)
            return null;

        foreach (var property in properties)
        {
            if (property.PropertyId != ChallengeScoreId)
                continue;

            try
            {
                if (property is IInt32Property intProperty)
                    return intProperty.Int32Value;
            }
            catch
            {
                // Ignore read failures.
            }
        }

        return null;
    }
}

public sealed class ExperienceReportSnapshot
{
    public string Title { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public string? UiDifficultyLabel { get; init; }
    public uint? UiDifficultyPropertyId { get; init; }
    public int? ChallengeScore { get; init; }
    public string Source { get; init; } = string.Empty;
    public ParsedExperienceReport Parsed { get; init; } = ParsedExperienceReport.Empty;
    public bool LooksComplete { get; init; }

    public bool IsVisible =>
        !string.IsNullOrWhiteSpace(Title)
        || !string.IsNullOrWhiteSpace(Text)
        || Parsed.HasUsableData
        || UiDifficultyPropertyId is > 0;
}
