using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class DeepSeekChat : MonoBehaviour
{
    [Header("API配置")]
    public string apiKey = "sk-286fcacd38b74e648785b4f770fffe4e";
    public string apiUrl = "https://api.deepseek.com/v1/chat/completions";

    [Header("UI引用")]
    public TMP_InputField userInputField;
    public TMP_Text chatOutputText;
    public ScrollRect scrollRect; // 用于控制滚动到末尾

    // 对话历史
    private List<Dictionary<string, string>> messages = new List<Dictionary<string, string>>();
    // 临时存储当前AI的流式响应内容
    private string currentBotResponse = "";
    // 标记是否正在接收流式数据
    private bool isStreaming = false;

    void Start()
    {
        // 初始化系统提示
        messages.Add(new Dictionary<string, string> { { "role", "system" }, { "content", "You are a helpful assistant. Answer concisely." } });
        // 确保主线程调度器存在
        if (UnityMainThreadDispatcher.Instance() == null)
        {
            new GameObject("UnityMainThreadDispatcher").AddComponent<UnityMainThreadDispatcher>();
        }
    }

    public void OnSendButtonClicked()
    {
        if (isStreaming) return; // 避免重复发送请求

        string userMessage = userInputField.text.Trim();
        if (string.IsNullOrEmpty(userMessage)) return;

        // 添加用户消息到历史并显示
        messages.Add(new Dictionary<string, string> { { "role", "user" }, { "content", userMessage } });
        chatOutputText.text += $"\nYou: {userMessage}";
        userInputField.text = "";

        // 准备接收AI的流式响应（先显示"AI: "前缀）
        currentBotResponse = "";
        chatOutputText.text += "\nAI: ";
        ScrollToBottom();

        // 开始流式请求
        StartCoroutine(StreamDeepSeekAPI());
    }

    /// <summary>
    /// 发起流式API请求并处理分块响应
    /// </summary>
    private IEnumerator StreamDeepSeekAPI()
    {
        isStreaming = true;

        // 构建请求数据（stream设为true）
        var requestData = new
        {
            model = "deepseek-chat",
            messages = messages,
            stream = true // 启用流式输出
        };
        string jsonData = JsonConvert.SerializeObject(requestData);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);

        // 创建请求
        UnityWebRequest request = new UnityWebRequest(apiUrl, "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        // 使用自定义DownloadHandler处理分块数据
        request.downloadHandler = new StreamDownloadHandler((data) =>
        {
            // 解析单块数据并更新UI（在主线程执行）
            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                ProcessStreamData(data);
            });
        });

        // 设置请求头
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + apiKey);

        // 发送请求并等待完成（流式响应会持续接收数据）
        yield return request.SendWebRequest();

        // 请求结束处理
        isStreaming = false;
        // 在请求失败的代码块中替换为：
        if (request.result != UnityWebRequest.Result.Success)
        {
            string errorDetails = request.downloadHandler.text; // 获取服务器返回的详细错误
            Debug.LogError($"请求错误: {request.error}，服务器详情: {errorDetails}");
            chatOutputText.text += $"\n[错误: {errorDetails}]";
        }
        else
        {
            // 流式结束后，将完整响应添加到对话历史
            messages.Add(new Dictionary<string, string> { { "role", "assistant" }, { "content", currentBotResponse } });
        }

        ScrollToBottom();
        request.Dispose(); // 释放资源
    }

    /// <summary>
    /// 处理单块流式数据
    /// </summary>
    private void ProcessStreamData(string data)
    {
        // DeepSeek流式响应格式：每行以"data:"开头，如"data: {\"id\":\"...\",\"choices\":[{\"delta\":{\"content\":\"xxx\"}}]}\n\n"
        // 最后一行是"data: [DONE]"
        string[] lines = data.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            string trimmedLine = line.TrimStart();
            if (!trimmedLine.StartsWith("data:")) continue;

            // 提取JSON部分（去掉"data: "前缀）
            string jsonPart = trimmedLine.Substring("data: ".Length).Trim();
            if (jsonPart == "[DONE]")
            {
                // 流式结束标志
                return;
            }

            try
            {
                // 解析流式响应片段（包含delta增量内容）
                var streamResponse = JsonConvert.DeserializeObject<StreamResponse>(jsonPart);
                if (streamResponse?.choices != null && streamResponse.choices.Length > 0)
                {
                    string deltaContent = streamResponse.choices[0].delta.content;
                    if (!string.IsNullOrEmpty(deltaContent))
                    {
                        // 累积增量内容并更新UI
                        currentBotResponse += deltaContent;
                        chatOutputText.text = chatOutputText.text.TrimEnd() + deltaContent; // 避免重复空格
                        ScrollToBottom(); // 滚动到最新内容
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"解析流式数据失败: {e.Message}，原始数据: {jsonPart}");
            }
        }
    }

    /// <summary>
    /// 滚动到文本末尾
    /// </summary>
    private void ScrollToBottom()
    {
        // 延迟一帧确保布局更新
        StartCoroutine(ScrollCoroutine());
    }

    private IEnumerator ScrollCoroutine()
    {
        yield return null;
        scrollRect.verticalNormalizedPosition = 0;
    }

    // 流式响应数据模型（仅包含需要的字段）
    private class StreamResponse
    {
        public StreamChoice[] choices;
    }

    private class StreamChoice
    {
        public Delta delta;
    }

    private class Delta
    {
        public string content; // 增量内容（逐字/逐句返回）
    }
}

// 自定义DownloadHandler，用于处理流式分块数据
public class StreamDownloadHandler : DownloadHandlerScript
{
    private Action<string> onDataReceived; // 数据接收回调
    private StringBuilder receivedData = new StringBuilder();

    public StreamDownloadHandler(Action<string> onDataReceived) : base(new byte[1024])
    {
        this.onDataReceived = onDataReceived;
    }

    // 当接收到数据块时调用
    protected override bool ReceiveData(byte[] data, int dataLength)
    {
        if (dataLength > 0)
        {
            // 将字节数据转换为字符串（UTF8编码）
            string text = Encoding.UTF8.GetString(data, 0, dataLength);
            receivedData.Append(text);

            // 触发回调，传递当前接收到的文本
            onDataReceived?.Invoke(text);
        }
        return true;
    }

    // 下载完成时调用（可选实现）
    protected override void CompleteContent()
    {
        base.CompleteContent();
    }

    // 重写获取数据的方法（可选，根据需求实现）
    protected override string GetText()
    {
        return receivedData.ToString();
    }
}

// 辅助类：确保UI操作在主线程执行
public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher instance;
    private Queue<Action> actions = new Queue<Action>();

    public static UnityMainThreadDispatcher Instance()
    {
        if (instance == null)
        {
            instance = FindObjectOfType<UnityMainThreadDispatcher>();
            if (instance == null)
            {
                GameObject obj = new GameObject("UnityMainThreadDispatcher");
                instance = obj.AddComponent<UnityMainThreadDispatcher>();
                DontDestroyOnLoad(obj); // 确保场景切换时不销毁
            }
        }
        return instance;
    }

    private void Update()
    {
        lock (actions)
        {
            while (actions.Count > 0)
            {
                actions.Dequeue().Invoke();
            }
        }
    }

    public void Enqueue(Action action)
    {
        lock (actions)
        {
            actions.Enqueue(action);
        }
    }
}