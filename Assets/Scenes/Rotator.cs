using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Rotator : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    private float angles = 0;
    // Update is called once per frame
    void Update()
    {
        angles += Time.deltaTime;
        if (angles > 50) { angles = 50; }
            transform.Rotate(angles,0 , 0);
    }
}
