namespace VirtualBank.Model.Common;

/// <summary>An operation would break a business rule. Never carries personal data.</summary>
public sealed class DomainRuleViolationException(string message) : Exception(message);
