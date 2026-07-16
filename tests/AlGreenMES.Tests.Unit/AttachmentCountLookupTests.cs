using AlGreenMES.Modules.Orders.Application.Queries.Tablet;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace AlGreenMES.Tests.Unit;

/// <summary>
/// Guards the per-item attachment counting behind the tablet queue/incoming/
/// active views (the batched replacement for the /attachments N+1). Must mirror
/// the FE AttachmentIndicator: order-level attachments (no OrderItemId) count
/// toward every item of the order, plus the item's own attachments.
/// </summary>
public class AttachmentCountLookupTests
{
    private static readonly Guid Order1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ItemA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ItemB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Order_level_attachments_count_for_every_item_of_that_order()
    {
        var lookup = AttachmentCountLookup.Build(new List<OrderAttachmentRef>
        {
            new(Order1, null),
            new(Order1, null),
        });

        lookup.CountFor(Order1, ItemA).Should().Be(2);
        lookup.CountFor(Order1, ItemB).Should().Be(2);
    }

    [Fact]
    public void Item_level_attachments_count_only_for_that_item()
    {
        var lookup = AttachmentCountLookup.Build(new List<OrderAttachmentRef>
        {
            new(Order1, ItemA),
        });

        lookup.CountFor(Order1, ItemA).Should().Be(1);
        lookup.CountFor(Order1, ItemB).Should().Be(0);
    }

    [Fact]
    public void Combines_order_level_and_item_level_like_the_fe_indicator()
    {
        var lookup = AttachmentCountLookup.Build(new List<OrderAttachmentRef>
        {
            new(Order1, null),   // order-level → counts for A and B
            new(Order1, ItemA),  // item A only
            new(Order1, ItemA),
        });

        lookup.CountFor(Order1, ItemA).Should().Be(3); // 1 order-level + 2 item
        lookup.CountFor(Order1, ItemB).Should().Be(1); // 1 order-level
    }

    [Fact]
    public void Empty_input_and_unknown_order_yield_zero()
    {
        AttachmentCountLookup.Build(new List<OrderAttachmentRef>())
            .CountFor(Order1, ItemA).Should().Be(0);
    }
}
