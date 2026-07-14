using VoK.Sdk.Ddo;
using VoK.Sdk.Enums;
using VoK.Sdk.Properties;

namespace DungeonTracker.Services;

public sealed class ExperienceReportCache
{
    private static readonly uint SummaryTitleId = (uint)DdoProperty.Dungeon_Summary_Title;
    private static readonly uint SummaryTextId = (uint)DdoProperty.Dungeon_Summary_Text;
    private static readonly uint UiDifficultyId = (uint)DdoProperty.Dungeon_UI_DifficultyName;
    private static readonly uint ChallengeScoreId = (uint)DdoProperty.Dungeon_Challenge_Score;

    private string _title = string.Empty;
    private string _text = string.Empty;
    private uint? _uiDifficultyPropertyId;
    private int? _challengeScore;
    private string _source = string.Empty;
    private DateTime? _updatedAtUtc;

    public void NoteProperty(IDdoGameDataProvider provider, IProperty property, string source)
    {
        switch (property.PropertyId)
        {
            case var id when id == SummaryTitleId:
                StoreTitle(PropertyTextReader.Read(provider, property), source);
                break;

            case var id when id == SummaryTextId:
                StoreText(PropertyTextReader.Read(provider, property), source);
                break;

            case var id when id == UiDifficultyId:
                StoreUiDifficulty(PortalDifficultyReader.TryReadPropertyId(property), source);
                break;

            case var id when id == ChallengeScoreId:
                StoreChallengeScore(ReadInt(property), source);
                break;
        }
    }

    public void Merge(ExperienceReportSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.Title))
            StoreTitle(snapshot.Title, snapshot.Source);

        if (!string.IsNullOrWhiteSpace(snapshot.Text))
            StoreText(snapshot.Text, snapshot.Source);

        if (snapshot.UiDifficultyPropertyId is uint propertyId)
            StoreUiDifficulty(propertyId, snapshot.Source);

        if (snapshot.ChallengeScore is int score)
            StoreChallengeScore(score, snapshot.Source);
    }

    public ExperienceReportSnapshot ToSnapshot()
    {
        var uiLabel = _uiDifficultyPropertyId is uint propertyId
            ? QuestDifficultyResolver.MapPropertyId(propertyId)
            : null;

        var parsed = ExperienceReportParser.Parse(_title, _text, uiLabel);

        return new ExperienceReportSnapshot
        {
            Title = _title,
            Text = _text,
            UiDifficultyLabel = uiLabel,
            UiDifficultyPropertyId = _uiDifficultyPropertyId,
            ChallengeScore = _challengeScore,
            Parsed = parsed,
            Source = string.IsNullOrWhiteSpace(_source) ? "cache" : _source,
            LooksComplete = parsed.LooksComplete || InstanceSummaryReader.LooksLikeCompletionSummary(_title, _text)
        };
    }

    public void Clear()
    {
        _title = string.Empty;
        _text = string.Empty;
        _uiDifficultyPropertyId = null;
        _challengeScore = null;
        _source = string.Empty;
        _updatedAtUtc = null;
    }

    public string DescribeState()
    {
        if (_updatedAtUtc == null)
            return "empty";

        return $"{_source} @ {_updatedAtUtc:HH:mm:ss} title={(_title.Length == 0 ? "—" : "yes")} text={(_text.Length == 0 ? "—" : "yes")}";
    }

    private void StoreTitle(string value, string source)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        _title = value;
        Touch(source);
    }

    private void StoreText(string value, string source)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        _text = value;
        Touch(source);
    }

    private void StoreUiDifficulty(uint? propertyId, string source)
    {
        if (propertyId is not > 0)
            return;

        _uiDifficultyPropertyId = propertyId;
        Touch(source);
    }

    private void StoreChallengeScore(int? score, string source)
    {
        if (score == null)
            return;

        _challengeScore = score;
        Touch(source);
    }

    private void Touch(string source)
    {
        _source = source;
        _updatedAtUtc = DateTime.UtcNow;
    }

    private static int? ReadInt(IProperty property)
    {
        try
        {
            if (property is IInt32Property intProperty)
                return intProperty.Int32Value;

            if (property is IUInt32Property uintProperty && uintProperty.UInt32Value is uint value)
                return (int)value;

            if (property.Value != null && int.TryParse(property.Value.ToString(), out var parsed))
                return parsed;
        }
        catch
        {
            // Ignore read failures.
        }

        return null;
    }
}
