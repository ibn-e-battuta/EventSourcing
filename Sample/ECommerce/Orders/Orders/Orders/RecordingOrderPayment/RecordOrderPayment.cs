using Core.Commands;
using Core.Marten.Repository;

namespace Orders.Orders.RecordingOrderPayment;

public record RecordOrderPayment(
    Guid OrderId,
    Guid PaymentId,
    DateTime PaymentRecordedAt
)
{
    public static RecordOrderPayment Create(Guid? orderId, Guid? paymentId, DateTime? paymentRecordedAt)
    {
        if (orderId == null || orderId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(orderId));
        if (paymentId == null || paymentId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(paymentId));
        if (paymentRecordedAt == null || paymentRecordedAt == default(DateTime))
            throw new ArgumentOutOfRangeException(nameof(paymentRecordedAt));

        return new RecordOrderPayment(orderId.Value, paymentId.Value, paymentRecordedAt.Value);
    }
}

public class HandleRecordOrderPayment:
    ICommandHandler<RecordOrderPayment>
{
    private readonly IMartenRepository<Order> orderRepository;

    public HandleRecordOrderPayment(IMartenRepository<Order> orderRepository) =>
        this.orderRepository = orderRepository;

    public Task Handle(RecordOrderPayment command, CancellationToken cancellationToken)
    {
        var (orderId, paymentId, recordedAt) = command;

        return orderRepository.GetAndUpdate(
            orderId,
            order => order.RecordPayment(paymentId, recordedAt),
            cancellationToken: cancellationToken
        );
    }
}
