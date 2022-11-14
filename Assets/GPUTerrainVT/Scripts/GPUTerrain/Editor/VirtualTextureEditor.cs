using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Experimental.Rendering;


[CustomEditor(typeof(RVT.VirtualTexture))]
public class VirtualTextureEditor : UnityEditor.Editor
{
   
    override public void OnInspectorGUI()
    {
        RVT.VirtualTexture vt = target as RVT.VirtualTexture;
        base.OnInspectorGUI();
        if (Application.isPlaying)
        {
            DrawTexture(vt.pageTableTexture, "PageTableTexture");
            DrawTexture(vt.physicsDiffuse, "PhysicsDiffuse");
            DrawTexture(vt.physicsNormal, "PhysicsNormal");
        }
    }


   
    protected void DrawTexture(Texture texture, string label = null)
    {
        if (texture == null)
            return;

        EditorGUILayout.Space();
        if (!string.IsNullOrEmpty(label))
        {
            EditorGUILayout.LabelField(label);
            EditorGUILayout.LabelField(string.Format("    Size: {0} X {1}", texture.width, texture.height));
        }
        else
        {
            EditorGUILayout.LabelField(string.Format("Size: {0} X {1}", texture.width, texture.height));
        }

        EditorGUI.DrawPreviewTexture(GUILayoutUtility.GetAspectRect((float)texture.width / texture.height), texture);
    }

};
