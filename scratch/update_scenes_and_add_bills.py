import re

# 1. Update 01Day.unity
with open('Assets/Scenes/01Day.unity', 'r', encoding='utf-8') as f:
    day_content = f.read()

# Update inflationRatePerDollar
day_content = day_content.replace('inflationRatePerDollar: 0.01', 'inflationRatePerDollar: 0.00025')

# Split blocks
day_blocks = day_content.split('--- !u!')

all_ids = set(re.findall(r'--- !u!\d+ &(\d+)', day_content))

# Update RectTransform 2079674435 and TMP 2079674433
for i in range(len(day_blocks)):
    if '224 &2079674435' in day_blocks[i]:
        b = day_blocks[i]
        b = b.replace('m_AnchoredPosition: {x: -55, y: -25}', 'm_AnchoredPosition: {x: -30, y: -25}')
        b = b.replace('m_SizeDelta: {x: 200, y: 50}', 'm_SizeDelta: {x: 350, y: 50}')
        b = b.replace('m_Pivot: {x: 0.5, y: 0.5}', 'm_Pivot: {x: 1, y: 1}')
        day_blocks[i] = b
    if '114 &2079674433' in day_blocks[i]:
        b = day_blocks[i]
        b = b.replace('m_HorizontalAlignment: 1', 'm_HorizontalAlignment: 4')
        b = b.replace('m_text: asdasd', 'm_text: "+0.00% Inflation"')
        day_blocks[i] = b

# Allocate unique IDs for Bills
allocated = []
curr = 7700000001
while len(allocated) < 45:
    if str(curr) not in all_ids:
        allocated.append(str(curr))
    curr += 1

bm_goid = allocated[0]
bm_trid = allocated[1]
bm_compid = allocated[2]

# Add bm_trid to interactables (201337191)
for i in range(len(day_blocks)):
    if '4 &201337191' in day_blocks[i]:
        b = day_blocks[i]
        lines = b.splitlines()
        for idx, l in enumerate(lines):
            if 'm_Children:' in l:
                lines.insert(idx + 1, f'  - {{fileID: {bm_trid}}}')
                break
        day_blocks[i] = '\n'.join(lines) + '\n'

# Build 5 bill cubes
bills_data = [
    {
        'type': 0,
        'name': 'Bill_Food',
        'title': 'Overdraft & Groceries Debt Notice',
        'price': '15',
        'desc': 'Debit card frozen! Food costs x2 until settled.'
    },
    {
        'type': 1,
        'name': 'Bill_Electric',
        'title': 'Final Notice: Electric Grid Disconnect',
        'price': '20',
        'desc': 'Voltage reduced! DVD player runs at 50% speed.'
    },
    {
        'type': 2,
        'name': 'Bill_Water',
        'title': 'Municipal Water Shutoff Warrant',
        'price': '15',
        'desc': 'Main water valve locked! Toilet is out of service.'
    },
    {
        'type': 3,
        'name': 'Bill_Sanitation',
        'title': 'Gas & Sanitation Delinquency Order',
        'price': '15',
        'desc': 'Hot water boiler cut off! Shower is unusable.'
    },
    {
        'type': 4,
        'name': 'Bill_CarPayment',
        'title': 'Vehicle Repossession & Wheel Clamp',
        'price': '25',
        'desc': 'Beater car immobilized! Cannot travel to the Bar.'
    }
]

bill_comp_ids = []
bill_tr_ids = []
new_blocks = []

id_idx = 3
for binfo in bills_data:
    b_goid = allocated[id_idx]
    b_trid = allocated[id_idx + 1]
    b_mfid = allocated[id_idx + 2]
    b_mrid = allocated[id_idx + 3]
    b_bcid = allocated[id_idx + 4]
    b_ioid = allocated[id_idx + 5]
    b_ivid = allocated[id_idx + 6]
    b_compid = allocated[id_idx + 7]
    id_idx += 8

    bill_comp_ids.append(b_compid)
    bill_tr_ids.append(b_trid)

    # GameObject
    go_block = f"""1 &{b_goid}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: {b_trid}}}
  - component: {{fileID: {b_mfid}}}
  - component: {{fileID: {b_mrid}}}
  - component: {{fileID: {b_bcid}}}
  - component: {{fileID: {b_ioid}}}
  - component: {{fileID: {b_ivid}}}
  - component: {{fileID: {b_compid}}}
  m_Layer: 0
  m_Name: {binfo['name']}
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 0
"""

    # Transform
    tr_block = f"""4 &{b_trid}
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {b_goid}}}
  serializedVersion: 2
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 0, z: 0}}
  m_LocalScale: {{x: 0.28, y: 0.38, z: 0.05}}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {{fileID: {bm_trid}}}
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
"""

    # MeshFilter
    mf_block = f"""33 &{b_mfid}
MeshFilter:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {b_goid}}}
  m_Mesh: {{fileID: 10202, guid: 0000000000000000e000000000000000, type: 0}}
"""

    # MeshRenderer
    mr_block = f"""23 &{b_mrid}
MeshRenderer:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {b_goid}}}
  m_Enabled: 1
  m_CastShadows: 1
  m_ReceiveShadows: 1
  m_DynamicOccludee: 1
  m_StaticShadowCaster: 0
  m_MotionVectors: 1
  m_LightProbeUsage: 1
  m_ReflectionProbeUsage: 1
  m_RayTracingMode: 2
  m_RayTraceProcedural: 0
  m_RayTracingAccelStructBuildFlagsOverride: 0
  m_RayTracingAccelStructBuildFlags: 1
  m_SmallMeshCulling: 1
  m_RenderingLayerMask: 1
  m_RendererPriority: 0
  m_Materials:
  - {{fileID: 2100000, guid: 8a303e05ecb9bcd46900f0b26e3d5a22, type: 2}}
  m_StaticBatchInfo:
    firstSubMesh: 0
    subMeshCount: 0
  m_StaticBatchRoot: {{fileID: 0}}
  m_ProbeAnchor: {{fileID: 0}}
  m_LightProbeVolumeOverride: {{fileID: 0}}
  m_ScaleInLightmap: 1
  m_ReceiveGI: 1
  m_PreserveUVs: 0
  m_IgnoreNormalsForChartDetection: 0
  m_ImportantGI: 0
  m_StitchLightmapSeams: 1
  m_SelectedEditorRenderState: 3
  m_MinimumChartSize: 4
  m_AutoUVMaxDistance: 0.5
  m_AutoUVMaxAngle: 89
  m_LightmapParameters: {{fileID: 0}}
  m_SortingLayerID: 0
  m_SortingLayer: 0
  m_SortingOrder: 0
  m_AdditionalVertexStreams: {{fileID: 0}}
"""

    # BoxCollider
    bc_block = f"""65 &{b_bcid}
BoxCollider:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {b_goid}}}
  m_Material: {{fileID: 0}}
  m_IncludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_ExcludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_LayerOverridePriority: 0
  m_IsTrigger: 0
  m_ProvidesContacts: 0
  m_Enabled: 1
  serializedVersion: 3
  m_Size: {{x: 1, y: 1, z: 1}}
  m_Center: {{x: 0, y: 0, z: 0}}
"""

    # InteractableObject
    io_block = f"""114 &{b_ioid}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {b_goid}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: 95062256367a0a648b5df2529e063804, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: Assembly-CSharp::InteractableObject
  hoverText: '{binfo['title']} - ${binfo['price']}.00\\n{binfo['desc']}\\n(Hold Click to Pay)'
  onClick:
    m_PersistentCalls:
      m_Calls: []
  hideHoverText: 0
"""

    # InteractVisualizer
    iv_block = f"""114 &{b_ivid}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {b_goid}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: 11d394c7a9ed6e949892e8495dcb9017, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: Assembly-CSharp::InteractVisualizer
  glowColor: {{r: 2, g: 0.15, b: 0.15, a: 1}}
  thickness: 0.035
  radialBlend: 0.5
  fillStrength: 0.6
  enablePeriodicGlow: 1
  periodicWait: 1.5
  periodicDuration: 1.2
"""

    # BillObject
    comp_block = f"""114 &{b_compid}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {b_goid}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: 13e560912bed4ef4b6f98ee8ecd63259, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: Assembly-CSharp::BillObject
  billType: {binfo['type']}
  billTitle: '{binfo['title']}'
  basePrice: {binfo['price']}
  penaltyDescription: '{binfo['desc']}'
  holdPrompt: Hold Click to Pay
  notEnoughMoneyPrompt: Not enough money!
  holdDuration: 1.2
  levitateHeight: 0.06
  returnDuration: 0.2
  shakeIntensity: 0.025
  shakeSpeed: 35
  shakeSound: {{fileID: 8300000, guid: 8c865c0401cccbe4fb6858789a87ae53, type: 3}}
  shakeVolume: 0.8
  paidSound: {{fileID: 8300000, guid: 157c61885f81d554db2dcc67316ef29a, type: 3}}
  paidVolume: 1
  cantAffordSound: {{fileID: 0}}
  poofParticlePrefab: {{fileID: 8184519965564883445, guid: cb9dec6170aeaac42afaa0013f4ba083, type: 3}}
  economyBouncer: {{fileID: 854348182134054070}}
"""
    new_blocks.extend([go_block, tr_block, mf_block, mr_block, bc_block, io_block, iv_block, comp_block])

# BillsManager Parent
bm_children_str = '\n'.join([f'  - {{fileID: {tid}}}' for tid in bill_tr_ids])
bm_objs_str = '\n'.join([f'  - {{fileID: {cid}}}' for cid in bill_comp_ids])

bm_go_block = f"""1 &{bm_goid}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: {bm_trid}}}
  - component: {{fileID: {bm_compid}}}
  m_Layer: 0
  m_Name: BillsManager
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
"""

bm_tr_block = f"""4 &{bm_trid}
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {bm_goid}}}
  serializedVersion: 2
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 5.25, y: 1.35, z: 0.58}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_ConstrainProportionsScale: 0
  m_Children:
{bm_children_str}
  m_Father: {{fileID: 201337191}}
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
"""

bm_comp_block = f"""114 &{bm_compid}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {bm_goid}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: 8d24cbbd4d694eac9fffaaccc36db845, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: Assembly-CSharp::BillManager
  billObjects:
{bm_objs_str}
  economyBouncer: {{fileID: 854348182134054070}}
  barManager: {{fileID: 1003284028}}
  foodBillMultiplier: 2
  electricSpeedMultiplier: 0.5
  activeBillIndex: -1
  isPaid: 0
"""

new_blocks.extend([bm_go_block, bm_tr_block, bm_comp_block])

# Append new blocks to 01Day
day_blocks.extend(new_blocks)
with open('Assets/Scenes/01Day.unity', 'w', encoding='utf-8') as f:
    f.write('--- !u!'.join(day_blocks))

print('01Day.unity updated successfully with BillsManager and 5 bill cubes!')

# 2. Update BarGameBarni.unity
with open('Assets/Barni/BarGameBarni.unity', 'r', encoding='utf-8') as f:
    bar_content = f.read()

bar_content = bar_content.replace('inflationRatePerDollar: 0.01', 'inflationRatePerDollar: 0.00025')
bar_blocks = bar_content.split('--- !u!')

for i in range(len(bar_blocks)):
    if '224 &2117530556' in bar_blocks[i]:
        b = bar_blocks[i]
        b = b.replace('m_AnchoredPosition: {x: -55, y: -25}', 'm_AnchoredPosition: {x: -30, y: -25}')
        b = b.replace('m_SizeDelta: {x: 200, y: 50}', 'm_SizeDelta: {x: 350, y: 50}')
        b = b.replace('m_Pivot: {x: 0.5, y: 0.5}', 'm_Pivot: {x: 1, y: 1}')
        bar_blocks[i] = b
    if '114 &2117530557' in bar_blocks[i]:
        b = bar_blocks[i]
        b = b.replace('m_HorizontalAlignment: 1', 'm_HorizontalAlignment: 4')
        b = b.replace('m_text: asdasd', 'm_text: "+0.00% Inflation"')
        bar_blocks[i] = b

with open('Assets/Barni/BarGameBarni.unity', 'w', encoding='utf-8') as f:
    f.write('--- !u!'.join(bar_blocks))

print('BarGameBarni.unity updated successfully!')
