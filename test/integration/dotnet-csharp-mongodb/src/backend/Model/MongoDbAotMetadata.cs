using System.Diagnostics.CodeAnalysis;

internal static class MongoDbAotMetadata
{
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties,
        typeof(WorkItemDocument))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties,
        typeof(PaymentDocument))]
    public static void PreserveDocumentMembers()
    {
    }
}
