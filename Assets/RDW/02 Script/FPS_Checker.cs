using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace _GCM
{
    public class FPS_Checker : MonoBehaviour
    {
        float deltaTime = 0.0f;

        GUIStyle style;
        Rect rect;
        float msec;
        float fps;
        float worstFps = 999f;
        string text;

        private bool tempb = true;

        void Awake()
        {
            //Application.targetFrameRate = -1;
            //Application.targetFrameRate = 90;

            int w = Screen.width, h = Screen.height;

            rect = new Rect(0, 0, w, h * 4 / 100);

            style = new GUIStyle();
            style.alignment = TextAnchor.UpperLeft;
            style.fontSize = h * 4 / 100;
            style.normal.textColor = Color.red;
        }

        private void OnDisable()
        {
            tempb = false;
            StopAllCoroutines();
        }

        private void Start()
        {
            StartCoroutine("worstReset");
        }

        //void Update()
        //{
        //    deltaTime += (Time.deltaTime - deltaTime) * 0.1f;
        //}

        void FixedUpdate()
        {
            deltaTime += (Time.fixedDeltaTime - deltaTime) * 0.1f;
        }

        void OnGUI()//�ҽ��� GUI ǥ��.
        {
            msec = deltaTime * 1000.0f;
            fps = 1.0f / deltaTime;  //�ʴ� ������ - 1�ʿ�

            if (fps < worstFps)  //���ο� ���� fps�� ���Դٸ� worstFps �ٲ���.
                worstFps = fps;
            text = msec.ToString("F2") + "ms (" + fps.ToString("F0") + ") //worst : " + worstFps.ToString("F1");
            GUI.Label(rect, text, style);
        }

        IEnumerator worstReset() //�ڷ�ƾ���� 15�� �������� ���� ������ ��������.
        {
            while (tempb)
            {
                yield return new WaitForSeconds(5.0f);
                worstFps = 999f;
            }
        }

    }
}