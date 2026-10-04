using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SRStudios.Tools{
[ExecuteInEditMode]
public class BathTub : MonoBehaviour
{
    public bool Blood;
    public bool DeadBody_1, DeadBody_2, DeadBody_3;
    public GameObject BloodObj;
    public GameObject Body_1, Body_2, Body_3;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {

        //............blood.........//

        if(Blood == true)
        {
            BloodObj.SetActive(true);
        }
        else
        {
            BloodObj.SetActive(false);
        }

        //.........dead body.................//

        if (DeadBody_1 == true)
        {
            Body_1.SetActive(true);
        }
        else
        {
            Body_1.SetActive(false);
        }


        if (DeadBody_2 == true)
        {
            Body_2.SetActive(true);
        }
        else
        {
            Body_2.SetActive(false);
        }



        if (DeadBody_3 == true)
        {
            Body_3.SetActive(true);
        }
        else
        {
            Body_3.SetActive(false);
        }


    }
}
}
