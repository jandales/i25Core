namespace i25Core.Core.Auditing
{
    /// <summary>
    /// Excludes a property, or a whole entity class, from automatic audit logging.
    /// Use it for secrets (password hashes, tokens) or noisy fields.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, Inherited = true)]
    public sealed class AuditIgnoreAttribute : Attribute
    {
    }
}
