namespace HanakaServer.Helpers;

public sealed record PublicRatingHistoryAttribution(
    long? RatedByUserId,
    string RatedByName,
    string? Note);

public static class RatingHistoryAttribution
{
    public const string SystemDisplayName = "Hệ thống";

    private const string HanakaStaffAuditPrefix = "nhân viên Hanaka userid:";
    private const string HanakaStaffNoteSeparator = ". Ghi chú:";
    private const int MaxStoredNoteLength = 500;

    public static string BuildHanakaStaffAuditNote(string note, string staffUserLabel)
    {
        var trimmedNote = (note ?? string.Empty).Trim();
        var staffLabel = string.IsNullOrWhiteSpace(staffUserLabel)
            ? "unknown"
            : staffUserLabel.Trim();

        var audit = $"{HanakaStaffAuditPrefix}{staffLabel}{HanakaStaffNoteSeparator} {trimmedNote}";
        return audit.Length <= MaxStoredNoteLength ? audit : audit[..MaxStoredNoteLength];
    }

    public static PublicRatingHistoryAttribution ForPublicDisplay(
        string? note,
        long? ratedByUserId,
        string? ratedByName)
    {
        if (IsHanakaStaffAuditNote(note))
        {
            return new PublicRatingHistoryAttribution(
                RatedByUserId: null,
                RatedByName: SystemDisplayName,
                Note: ExtractHanakaStaffNote(note));
        }

        return new PublicRatingHistoryAttribution(
            RatedByUserId: ratedByUserId,
            RatedByName: ratedByUserId == null
                ? SystemDisplayName
                : ratedByName ?? string.Empty,
            Note: note);
    }

    private static bool IsHanakaStaffAuditNote(string? note)
    {
        return note?.StartsWith(HanakaStaffAuditPrefix, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string? ExtractHanakaStaffNote(string? auditNote)
    {
        if (string.IsNullOrWhiteSpace(auditNote))
        {
            return null;
        }

        var separatorIndex = auditNote.IndexOf(HanakaStaffNoteSeparator, StringComparison.OrdinalIgnoreCase);
        if (separatorIndex < 0)
        {
            return null;
        }

        var publicNote = auditNote[(separatorIndex + HanakaStaffNoteSeparator.Length)..].Trim();
        return string.IsNullOrWhiteSpace(publicNote) ? null : publicNote;
    }
}
