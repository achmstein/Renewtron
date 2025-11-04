using System.Text.Json.Serialization;

namespace Renewtron.Services;

public class OntraportConversationResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("data")]
    public ConversationData? Data { get; set; }

    [JsonPropertyName("account_id")]
    public int AccountId { get; set; }
}

public class ConversationData
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("max_date")]
    public string? MaxDate { get; set; }

    [JsonPropertyName("hasNext")]
    public bool HasNext { get; set; }

    [JsonPropertyName("hasPrev")]
    public bool HasPrev { get; set; }

    [JsonPropertyName("messages")]
    public List<ConversationMessage> Messages { get; set; } = new();

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

public class ConversationMessage
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("contact_id")]
    public string? ContactId { get; set; }

    [JsonPropertyName("item_id")]
    public string? ItemId { get; set; }

    [JsonPropertyName("merge_data")]
    public string? MergeData { get; set; }

    [JsonPropertyName("vtype")]
    public string? VType { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("attachments")]
    public string? Attachments { get; set; }

    [JsonPropertyName("cc")]
    public string? Cc { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("uid")]
    public string? Uid { get; set; }

    [JsonPropertyName("object_type_id")]
    public string? ObjectTypeId { get; set; }

    [JsonPropertyName("topic")]
    public string? Topic { get; set; }

    [JsonPropertyName("sender_data")]
    public string? SenderData { get; set; }

    [JsonPropertyName("thread_meta")]
    public string? ThreadMeta { get; set; }

    [JsonPropertyName("resource")]
    public string? Resource { get; set; }
}
