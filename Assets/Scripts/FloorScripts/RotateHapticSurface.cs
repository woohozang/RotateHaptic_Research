using UnityEngine;

public class RotateHapticSurface : MonoBehaviour
{
    [Header("참조 설정")]
    public RotateHapticCombinedV2 hapticScript;
    public Transform wheelPosition; // 바퀴의 위치 (없으면 이 스크립트의 위치 사용)

    [Header("지면 감지 설정")]
    public float rayDistance = 0.3f; // 바닥까지의 거리
    public LayerMask floorLayer;    // 바닥만 감지할 레이어 (선택 사항)

    [Header("재질별 주파수")]
    public float defaultFreq = 0.35f;
    public float tileFreq = 0.8f;
    public float asphaltFreq = 0.25f;
    public float carpetFreq = 0.15f;

    private string _lastTag = "";

    void Update()
    {
        if (hapticScript == null) return;

        // 바퀴 위치에서 아래로 레이를 쏩니다.
        Vector3 origin = wheelPosition ? wheelPosition.position : transform.position;
        RaycastHit hit;

        // 레이가 바닥에 맞았을 때
        if (Physics.Raycast(origin, Vector3.down, out hit, rayDistance))
        {
            string currentTag = hit.collider.tag;

            // 태그가 바뀔 때만 로그 출력 및 주파수 변경
            if (_lastTag != currentTag)
            {
                UpdateFrequency(currentTag);
                _lastTag = currentTag;
            }
        }
        else
        {
            // 공중에 떠 있을 때
            if (_lastTag != "Air")
            {
                hapticScript.baseFreq = defaultFreq;
                Debug.Log("<color=red>[Surface] 완전히 바닥에서 벗어남 (공중)</color>");
                _lastTag = "Air";
            }
        }
    }

    private void UpdateFrequency(string tag)
    {
        switch (tag)
        {
            case "Floor_Tile":
                hapticScript.baseFreq = tileFreq;
                Debug.Log($"<color=#00FFFF>[Surface] 감지 성공: <b>타일</b></color> (Freq: {tileFreq})");
                break;
            case "Floor_Asphalt":
                hapticScript.baseFreq = asphaltFreq;
                Debug.Log($"<color=#FFA500>[Surface] 감지 성공: <b>아스팔트</b></color> (Freq: {asphaltFreq})");
                break;
            case "Floor_Carpet":
                hapticScript.baseFreq = carpetFreq;
                Debug.Log($"<color=#C0C0C0>[Surface] 감지 성공: <b>카펫</b></color>");
                break;
            default:
                hapticScript.baseFreq = defaultFreq;
                Debug.Log($"[Surface] 기본 바닥 감지: {tag}");
                break;
        }
    }

    // 에디터 뷰에서 레이가 잘 닿는지 확인하기 위한 시각화
    void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Vector3 origin = wheelPosition ? wheelPosition.position : transform.position;
        Gizmos.DrawRay(origin, Vector3.down * rayDistance);
    }
}