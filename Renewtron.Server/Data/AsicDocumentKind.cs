namespace Renewtron.Data;

/// <summary>Which ASIC letter a notification's PDF is. Every one of them carries the ASIC key.</summary>
public enum AsicDocumentKind
{
    /// <summary>Not recognised, or the PDF hasn't been read yet.</summary>
    Other,
    /// <summary>"Here is the ASIC Key for NAME: 1-…" — the reply to a key request.</summary>
    KeyLetter,
    /// <summary>"Business name renewal notice for 'NAME'" — ASIC's invoice to renew.</summary>
    RenewalNotice,
    /// <summary>"Registration of business name renewed for 'NAME'" — proof of renewal.</summary>
    RenewalConfirmation,
}
