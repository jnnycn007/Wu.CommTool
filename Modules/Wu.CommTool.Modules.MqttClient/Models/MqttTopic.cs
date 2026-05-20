namespace Wu.CommTool.Modules.MqttClient.Models;

/// <summary>
/// Mqtt的主题 需要主题作为属性才能实现修改通知
/// </summary>
public partial class MqttTopic : ObservableObject
{
    public MqttTopic(string topic,bool noLocal = false)
    {
        Topic = topic;
        NoLocal = noLocal;
    }

    public MqttTopic(MqttTopic topic)
    {
        Topic = topic.Topic;
        NoLocal = topic.NoLocal;
        QosLevel = topic.QosLevel;
    }

    /// <summary>
    /// 主题
    /// </summary>
    [ObservableProperty] string topic = string.Empty;

    /// <summary>
    /// NoLocal true=不接收自己发布的消息 false=接收自己发布的消息
    /// </summary>
    [ObservableProperty] bool noLocal = false;

    /// <summary>
    /// 消息质量等级
    /// </summary>
    [ObservableProperty] QosLevel qosLevel = QosLevel.Qos1;

    public MqttTopic Clone()
    {
        return new MqttTopic(this);
    }

    public override string ToString()
    {
        return $"{Topic} NoLocal:{NoLocal} Qos:{QosLevel}";
    }
}
