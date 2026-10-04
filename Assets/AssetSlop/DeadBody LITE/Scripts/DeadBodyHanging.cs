using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SRStudios.Tools{
[ExecuteInEditMode]
public class DeadBodyHanging : MonoBehaviour
{

    public float rope_length;
    public Transform rope;
    // Start is called before the first frame update
    void Start()
    {
       

    }

    // Update is called once per frame


    void Update()
    {
        rope.transform.localPosition = new Vector3(0, rope_length, 0);

    }
}
}
