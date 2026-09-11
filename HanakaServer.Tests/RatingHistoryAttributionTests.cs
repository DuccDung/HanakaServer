using HanakaServer.Helpers;
using Xunit;

namespace HanakaServer.Tests;

public sealed class RatingHistoryAttributionTests
{
    [Fact]
    public void Hanaka_staff_rating_is_presented_as_system_without_staff_identity()
    {
        var result = RatingHistoryAttribution.ForPublicDisplay(
            "nhân viên Hanaka userid:42. Ghi chú: Chấm sau buổi kiểm tra kỹ thuật.",
            ratedByUserId: 42,
            ratedByName: "Nhân viên A");

        Assert.Null(result.RatedByUserId);
        Assert.Equal("Hệ thống", result.RatedByName);
        Assert.Equal("Chấm sau buổi kiểm tra kỹ thuật.", result.Note);
    }

    [Fact]
    public void Non_staff_rating_keeps_its_original_attribution()
    {
        var result = RatingHistoryAttribution.ForPublicDisplay(
            "Người chơi tự cập nhật điểm trình.",
            ratedByUserId: 15,
            ratedByName: "Nguyễn Văn A");

        Assert.Equal(15, result.RatedByUserId);
        Assert.Equal("Nguyễn Văn A", result.RatedByName);
        Assert.Equal("Người chơi tự cập nhật điểm trình.", result.Note);
    }

    [Fact]
    public void Staff_audit_note_format_remains_available_for_internal_traceability()
    {
        var result = RatingHistoryAttribution.BuildHanakaStaffAuditNote(
            "  Quan sát tại sân số 2.  ",
            "99");

        Assert.Equal(
            "nhân viên Hanaka userid:99. Ghi chú: Quan sát tại sân số 2.",
            result);
    }
}
