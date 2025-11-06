using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CameraController : MonoBehaviour
{
    [Header("Camera Settings")]
    public int requestWidth = 1280;
    public int requestHeight = 720;
    public int requestFPS = 30;

    [Header("UI References")]
    public RawImage cameraDisplay; // 用于UI显示
    public GameObject startButton;
    public GameObject stopButton;
    public GameObject takePhotoButton;
    public GameObject switchButton; // 添加切换按钮引用
    public Text statusText;
    public Text cameraInfoText; // 显示当前相机信息

    [Header("Debug Settings")]
    public bool enableDebugInfo = true;
    public bool useDeviceSpecificFix = true;

    private WebCamTexture webCamTexture;
    private bool isCameraAvailable = false;
    private List<WebCamDevice> availableCameras = new List<WebCamDevice>();
    private int currentCameraIndex = 0;

    void Start()
    {
#if UNITY_EDITOR
        // 使用模拟数据或WebCamTexture
        if (UnityEditor.EditorApplication.isRemoteConnected)
        {
            StartCoroutine(InitializeCamera());
        }
        else
        {
            // 使用模拟相机或测试纹理
            SetupMockCamera();
        }
#else
        StartCoroutine(InitializeCamera());
#endif

        // 自动查找UI组件（如果未在Inspector中分配）
        FindUIComponents();

        UpdateStatus("准备启动相机...");
        UpdateButtonStates(false);
    }

    void FindUIComponents()
    {
        if (cameraDisplay == null)
            cameraDisplay = FindObjectOfType<RawImage>();

        if (statusText == null)
        {
            Text[] texts = FindObjectsOfType<Text>();
            foreach (Text text in texts)
            {
                if (text.name.Contains("Status"))
                {
                    statusText = text;
                    break;
                }
            }
        }

        // 查找相机信息文本
        if (cameraInfoText == null)
        {
            Text[] texts = FindObjectsOfType<Text>();
            foreach (Text text in texts)
            {
                if (text.name.Contains("CameraInfo") || text.name.Contains("Camera"))
                {
                    cameraInfoText = text;
                    break;
                }
            }
        }
    }

    void SetupMockCamera()
    {
        // 创建测试纹理
        Texture2D testTexture = new Texture2D(512, 512);

        // 填充测试纹理
        for (int y = 0; y < testTexture.height; y++)
        {
            for (int x = 0; x < testTexture.width; x++)
            {
                Color color = new Color(
                    (float)x / testTexture.width,
                    (float)y / testTexture.height,
                    Mathf.Sin(Time.time) * 0.5f + 0.5f,
                    1.0f
                );
                testTexture.SetPixel(x, y, color);
            }
        }
        testTexture.Apply();

        if (cameraDisplay != null)
        {
            cameraDisplay.texture = testTexture;
        }
        else
        {
            GetComponent<Renderer>().material.mainTexture = testTexture;
        }

        Debug.Log("使用模拟相机（Unity Remote未连接）");
        UpdateStatus("模拟相机模式");
    }

    // 公开方法：启动相机
    public void StartCamera()
    {
        StartCoroutine(InitializeCamera());
    }

    // 公开方法：停止相机
    public void StopCamera()
    {
        if (webCamTexture != null && webCamTexture.isPlaying)
        {
            webCamTexture.Stop();
            UpdateStatus("相机已停止");
        }
        UpdateButtonStates(false);
        UpdateCameraInfo("");
    }

    // 公开方法：拍照
    public void TakePhoto()
    {
        if (webCamTexture != null && webCamTexture.isPlaying)
        {
            StartCoroutine(CapturePhoto());
        }
    }

    // 初始化相机协程
    private IEnumerator InitializeCamera()
    {
        UpdateStatus("请求相机权限...");

        // 请求相机权限
        yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            UpdateStatus("用户拒绝了相机权限");
            yield break;
        }

        UpdateStatus("搜索相机设备...");

        // 获取可用相机设备
        WebCamDevice[] devices = WebCamTexture.devices;
        availableCameras.Clear();

        if (devices.Length == 0)
        {
            UpdateStatus("没有找到可用的相机设备");
            yield break;
        }

        // 存储所有可用相机
        availableCameras.AddRange(devices);

        // 显示所有可用设备信息
        string devicesInfo = $"找到 {devices.Length} 个相机设备:\n";
        for (int i = 0; i < devices.Length; i++)
        {
            string cameraType = GetCameraType(devices[i]);
            devicesInfo += $"{i}: {devices[i].name} ({cameraType})\n";
        }
        Debug.Log(devicesInfo);

        // 默认选择第一个相机
        currentCameraIndex = 0;
        yield return StartCoroutine(StartSelectedCamera());
    }

    // 启动选中的相机
    private IEnumerator StartSelectedCamera()
    {
        if (availableCameras.Count == 0 || currentCameraIndex >= availableCameras.Count)
        {
            UpdateStatus("没有可用的相机设备");
            yield break;
        }

        // 停止当前相机
        if (webCamTexture != null && webCamTexture.isPlaying)
        {
            webCamTexture.Stop();
        }

        WebCamDevice selectedDevice = availableCameras[currentCameraIndex];
        UpdateStatus($"启动相机: {selectedDevice.name}");

        // 创建并启动WebCamTexture
        webCamTexture = new WebCamTexture(selectedDevice.name, requestWidth, requestHeight, requestFPS);

        // 设置显示
        if (cameraDisplay != null)
        {
            cameraDisplay.texture = webCamTexture;
        }
        else
        {
            // 使用3D对象的材质显示
            GetComponent<Renderer>().material.mainTexture = webCamTexture;
        }

        webCamTexture.Play();

        // 等待相机启动
        yield return new WaitUntil(() => webCamTexture.width > 100);
        yield return new WaitForSeconds(0.5f); // 额外等待确保相机稳定

        // 多次尝试调整方向（有些设备需要时间）
        for (int i = 0; i < 3; i++)
        {
            if (useDeviceSpecificFix)
            {
                AdjustDisplayOrientationByDevice(); // 使用设备特定修复
            }
            else
            {
                AdjustDisplayOrientationEnhanced(); // 使用增强版通用修复
            }
            yield return new WaitForSeconds(0.1f);
        }

        isCameraAvailable = true;

        // 更新相机信息
        UpdateCameraInfo($"{selectedDevice.name}\n{GetCameraType(selectedDevice)}\n{webCamTexture.width}x{webCamTexture.height}");

        UpdateStatus($"相机运行中: {webCamTexture.width}x{webCamTexture.height}");
        UpdateButtonStates(true);

        // 调试信息
        if (enableDebugInfo)
        {
            DebugCameraOrientation();
        }
    }

    // 获取相机类型描述
    private string GetCameraType(WebCamDevice device)
    {
        if (device.isFrontFacing)
        {
            return "前置摄像头";
        }
        else
        {
            // 尝试根据设备名称判断其他类型
            string name = device.name.ToLower();
            if (name.Contains("back") || name.Contains("rear") || name.Contains("主"))
                return "后置主摄像头";
            else if (name.Contains("wide") || name.Contains("广角"))
                return "广角摄像头";
            else if (name.Contains("tele") || name.Contains("长焦"))
                return "长焦摄像头";
            else if (name.Contains("ultra") || name.Contains("超广角"))
                return "超广角摄像头";
            else if (name.Contains("depth") || name.Contains("深度"))
                return "景深摄像头";
            else if (name.Contains("macro") || name.Contains("微距"))
                return "微距摄像头";
            else
                return "后置摄像头";
        }
    }

    // ========== 相机方向修正方法 ==========

    // 增强版方向修正方法
    private void AdjustDisplayOrientationEnhanced()
    {
        if (cameraDisplay == null || webCamTexture == null) return;

        try
        {
            int videoRotationAngle = webCamTexture.videoRotationAngle;
            bool isVerticallyMirrored = webCamTexture.videoVerticallyMirrored;

            Debug.Log($"相机方向调试: 旋转角度={videoRotationAngle}°, 垂直镜像={isVerticallyMirrored}, 分辨率={webCamTexture.width}x{webCamTexture.height}");

            // 重置所有变换
            cameraDisplay.transform.localRotation = Quaternion.identity;
            cameraDisplay.transform.localScale = Vector3.one;

            // 应用旋转
            cameraDisplay.transform.rotation = Quaternion.AngleAxis(videoRotationAngle, Vector3.forward);

            // 根据设备和旋转状态设置UV矩形
            Rect uvRect = CalculateUVRect(videoRotationAngle, isVerticallyMirrored);
            cameraDisplay.uvRect = uvRect;

            // 更新UI显示信息
            UpdateCameraOrientationInfo(videoRotationAngle, isVerticallyMirrored);

            // 调整宽高比
            AdjustAspectRatio();

        }
        catch (System.Exception e)
        {
            Debug.LogError($"调整相机方向时出错: {e.Message}");
        }
    }

    // 计算UV矩形
    private Rect CalculateUVRect(int rotationAngle, bool isMirrored)
    {
        switch (rotationAngle)
        {
            case 0:
                return isMirrored ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1);

            case 90:
                return isMirrored ? new Rect(1, 1, -1, -1) : new Rect(0, 0, 1, 1);

            case 180:
                return isMirrored ? new Rect(1, 0, -1, 1) : new Rect(1, 1, -1, -1);

            case 270:
                return isMirrored ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1);

            default:
                return new Rect(0, 0, 1, 1);
        }
    }

    // 设备特定的方向修正
    private void AdjustDisplayOrientationByDevice()
    {
        if (cameraDisplay == null || webCamTexture == null) return;

        string deviceModel = SystemInfo.deviceModel.ToLower();
        int videoRotationAngle = webCamTexture.videoRotationAngle;
        bool isVerticallyMirrored = webCamTexture.videoVerticallyMirrored;

        Debug.Log($"设备: {deviceModel}, 旋转: {videoRotationAngle}°, 镜像: {isVerticallyMirrored}");

        // 重置变换
        cameraDisplay.transform.localRotation = Quaternion.identity;
        cameraDisplay.uvRect = new Rect(0, 0, 1, 1);

            // 通用修复
            ApplyGenericFix(videoRotationAngle, isVerticallyMirrored);
        

        AdjustAspectRatio();
        UpdateCameraOrientationInfo(videoRotationAngle, isVerticallyMirrored);
    }



    private void ApplyGenericFix(int rotationAngle, bool isMirrored)
    {
        cameraDisplay.transform.rotation = Quaternion.AngleAxis(rotationAngle, Vector3.forward);

        if (isMirrored)
        {
            cameraDisplay.uvRect = new Rect(0, 1, 1, 1); // 垂直翻转
        }
        else
        {
            cameraDisplay.uvRect = new Rect(0, 0, 1, 1); // 正常
        }
    }

    // 调整宽高比以保持正确比例
    private void AdjustAspectRatio()
    {
        if (cameraDisplay != null && webCamTexture != null && webCamTexture.width > 100)
        {
            RectTransform rectTransform = cameraDisplay.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                float aspectRatio = (float)webCamTexture.width / webCamTexture.height;

                // 根据旋转角度调整宽高比
                int videoRotationAngle = webCamTexture.videoRotationAngle;
                if (videoRotationAngle == 90 || videoRotationAngle == 270)
                {
                    aspectRatio = (float)webCamTexture.height / webCamTexture.width;
                }

                // 保持高度不变，调整宽度
                float newWidth = rectTransform.sizeDelta.y * aspectRatio;
                rectTransform.sizeDelta = new Vector2(newWidth, rectTransform.sizeDelta.y);
            }
        }
    }

    // 更新相机方向信息
    private void UpdateCameraOrientationInfo(int rotationAngle, bool isMirrored)
    {
        string orientationInfo = $"旋转: {rotationAngle}°\n镜像: {(isMirrored ? "是" : "否")}";

        if (cameraInfoText != null)
        {
            // 保留原有信息，添加方向信息
            string currentInfo = cameraInfoText.text;
            if (!string.IsNullOrEmpty(currentInfo))
            {
                string[] lines = currentInfo.Split('\n');
                if (lines.Length > 0)
                {
                    orientationInfo = $"{lines[0]}\n{orientationInfo}";
                }
            }
            cameraInfoText.text = orientationInfo;
        }
    }

    // ========== 相机切换功能 ==========

    // 切换到下一个相机
    public void SwitchToNextCamera()
    {
        if (availableCameras.Count <= 1)
        {
            UpdateStatus("只有一个相机设备，无法切换");
            return;
        }

        currentCameraIndex = (currentCameraIndex + 1) % availableCameras.Count;
        StartCoroutine(StartSelectedCamera());
    }

    // 切换到上一个相机
    public void SwitchToPreviousCamera()
    {
        if (availableCameras.Count <= 1)
        {
            UpdateStatus("只有一个相机设备，无法切换");
            return;
        }

        currentCameraIndex = (currentCameraIndex - 1 + availableCameras.Count) % availableCameras.Count;
        StartCoroutine(StartSelectedCamera());
    }

    // 切换到特定索引的相机
    public void SwitchToCamera(int index)
    {
        if (index < 0 || index >= availableCameras.Count)
        {
            Debug.LogError($"相机索引超出范围: {index}，可用相机数量: {availableCameras.Count}");
            return;
        }

        currentCameraIndex = index;
        StartCoroutine(StartSelectedCamera());
    }

    // 切换到前置摄像头
    public void SwitchToFrontCamera()
    {
        for (int i = 0; i < availableCameras.Count; i++)
        {
            if (availableCameras[i].isFrontFacing)
            {
                currentCameraIndex = i;
                StartCoroutine(StartSelectedCamera());
                return;
            }
        }
        UpdateStatus("未找到前置摄像头");
    }

    // 切换到后置摄像头
    public void SwitchToBackCamera()
    {
        for (int i = 0; i < availableCameras.Count; i++)
        {
            if (!availableCameras[i].isFrontFacing)
            {
                currentCameraIndex = i;
                StartCoroutine(StartSelectedCamera());
                return;
            }
        }
        UpdateStatus("未找到后置摄像头");
    }

    // 获取当前相机信息
    public string GetCurrentCameraInfo()
    {
        if (availableCameras.Count == 0 || currentCameraIndex >= availableCameras.Count)
            return "无相机信息";

        WebCamDevice device = availableCameras[currentCameraIndex];
        return $"{device.name} ({GetCameraType(device)})";
    }

    // 获取所有相机信息
    public List<string> GetAllCameraInfo()
    {
        List<string> cameraInfos = new List<string>();
        for (int i = 0; i < availableCameras.Count; i++)
        {
            cameraInfos.Add($"{i}: {availableCameras[i].name} ({GetCameraType(availableCameras[i])})");
        }
        return cameraInfos;
    }

    // ========== 拍照功能 ==========

    // 拍照协程
    private IEnumerator CapturePhoto()
    {
        UpdateStatus("拍照中...");

        yield return new WaitForEndOfFrame();

        Texture2D photo = new Texture2D(webCamTexture.width, webCamTexture.height);
        photo.SetPixels(webCamTexture.GetPixels());
        photo.Apply();

        byte[] bytes = photo.EncodeToJPG(85);
        string filename = $"photo_{System.DateTime.Now:yyyyMMdd_HHmmss}.jpg";
        string path = System.IO.Path.Combine(Application.persistentDataPath, filename);

        System.IO.File.WriteAllBytes(path, bytes);

        // 打印完整路径
        Debug.Log($"照片保存路径: {path}");
        Debug.Log($"持久化数据路径: {Application.persistentDataPath}");
        Debug.Log($"使用相机: {GetCurrentCameraInfo()}");

        UpdateStatus($"照片已保存: {filename}\n路径: {path}\n相机: {GetCurrentCameraInfo()}");

        Destroy(photo);
    }

    // ========== UI 管理 ==========

    // 更新按钮状态
    private void UpdateButtonStates(bool isCameraRunning)
    {
        if (startButton != null)
            startButton.SetActive(!isCameraRunning);

        if (stopButton != null)
            stopButton.SetActive(isCameraRunning);

        if (takePhotoButton != null)
            takePhotoButton.SetActive(isCameraRunning);

        // 只有多个相机时才显示切换按钮
        if (switchButton != null)
            switchButton.SetActive(isCameraRunning && availableCameras.Count > 1);
    }

    // 更新状态文本
    private void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log($"Camera Status: {message}");
    }

    // 更新相机信息文本
    private void UpdateCameraInfo(string info)
    {
        if (cameraInfoText != null)
        {
            cameraInfoText.text = info;
        }
    }

    // ========== 调试功能 ==========

    // 调试相机方向
    public void DebugCameraOrientation()
    {
        if (webCamTexture != null)
        {
            string debugInfo = $"相机方向调试:\n" +
                              $"旋转角度: {webCamTexture.videoRotationAngle}°\n" +
                              $"垂直镜像: {webCamTexture.videoVerticallyMirrored}\n" +
                              $"分辨率: {webCamTexture.width}x{webCamTexture.height}\n" +
                              $"设备: {SystemInfo.deviceModel}\n" +
                              $"是否播放: {webCamTexture.isPlaying}";

            Debug.Log(debugInfo);
            if (enableDebugInfo)
            {
                UpdateStatus(debugInfo);
            }
        }
    }

    // 手动修正方向（用于测试）
    public void ManualFixOrientation()
    {
        if (webCamTexture != null && webCamTexture.isPlaying)
        {
            if (useDeviceSpecificFix)
            {
                AdjustDisplayOrientationByDevice();
            }
            else
            {
                AdjustDisplayOrientationEnhanced();
            }
            UpdateStatus("手动修正相机方向完成");
        }
    }

    // 在UI上显示相机列表（可选功能）
    public void ShowCameraList()
    {
        string cameraList = "可用相机:\n";
        for (int i = 0; i < availableCameras.Count; i++)
        {
            string currentIndicator = (i == currentCameraIndex) ? " [当前]" : "";
            cameraList += $"{i}: {availableCameras[i].name} ({GetCameraType(availableCameras[i])}){currentIndicator}\n";
        }
        UpdateStatus(cameraList);
    }

    // ========== 生命周期管理 ==========

    // 清理资源
    void OnDestroy()
    {
        if (webCamTexture != null)
        {
            if (webCamTexture.isPlaying)
            {
                webCamTexture.Stop();
            }
            Destroy(webCamTexture);
        }
    }

    void Update()
    {
        // 键盘快捷键
        if (Input.GetKeyDown(KeyCode.Space))
        {
            TakePhoto();
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            SwitchToNextCamera();
        }

        if (Input.GetKeyDown(KeyCode.V))
        {
            SwitchToPreviousCamera();
        }

        if (Input.GetKeyDown(KeyCode.F))
        {
            SwitchToFrontCamera();
        }

        if (Input.GetKeyDown(KeyCode.B))
        {
            SwitchToBackCamera();
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            ManualFixOrientation(); // R键手动修正方向
        }

        if (Input.GetKeyDown(KeyCode.D))
        {
            DebugCameraOrientation(); // D键显示调试信息
        }

        // 数字键切换特定相机
        for (int i = 0; i < Mathf.Min(availableCameras.Count, 9); i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                SwitchToCamera(i);
            }
        }
    }
}