"""Read-only source/asset checks for the Combat module; not Unity compilation or play tests."""
from pathlib import Path
import json
import re
import sys

MODULE=Path(__file__).resolve().parents[1]
PACKAGE=MODULE.parents[1]
INTEGRATIONS=PACKAGE/'Integrations/EasyCombat'
errors=[]
def check(condition,message):
    if not condition: errors.append(message)
check(INTEGRATIONS.is_dir(),'Missing shipped Combat integrations')
check(not (MODULE/'package.json').exists(),'Combat must not contain a nested package manifest')
check((MODULE/'LICENSE.md').read_text(encoding='utf-8-sig').startswith('MIT License'),'Missing MIT license')
roots=[MODULE,INTEGRATIONS]
assemblies={}
for root in roots:
    for path in root.rglob('*.asmdef'):
        data=json.loads(path.read_text(encoding='utf-8-sig')); name=data['name']
        check(name not in assemblies,'Duplicate assembly '+name)
        assemblies[name]=(path,data)
        check(not any(d['name'].startswith('com.oreethehat.') for d in data.get('versionDefines',[])),'Stale module package guard '+name)
        check(not any('EASY_WAVE' in d for d in data.get('defineConstraints',[])),'Stale define constraint '+name)
        check(not any(r.startswith('GamesFactory.') for r in data.get('references',[])),'Project dependency '+name)
        if 'TestAssemblies' in data.get('optionalUnityReferences',[]):
            check('UNITY_INCLUDE_TESTS' in data.get('defineConstraints',[]),'Test assembly without include-tests guard '+name)
        if name.endswith('.Editor') or name=='EasyCombat.EditorToolsTests':
            check(data.get('includePlatforms')==['Editor'],'Editor assembly enters Player '+name)
known={json.loads(p.read_text(encoding='utf-8-sig'))['name'] for p in PACKAGE.rglob('*.asmdef')}
for name,(path,data) in assemblies.items():
    for ref in data.get('references',[]):
        if ref.startswith('Easy'): check(ref in known,'Unresolved internal assembly '+name+' -> '+ref)
    if any(name.startswith(prefix) for prefix in ['EasyCombat.EasyResources','EasyCombat.EasyFeedbacks','EasyCombat.EasyMovement']) and not name.endswith('.Editor'):
        check(INTEGRATIONS in path.parents,'Production adapter/test still hidden in Samples: '+name)
visiting=set(); done=set()
def visit(name):
    if name in visiting: errors.append('Assembly cycle at '+name); return
    if name in done: return
    visiting.add(name)
    for ref in assemblies[name][1].get('references',[]):
        if ref in assemblies: visit(ref)
    visiting.remove(name); done.add(name)
for name in assemblies: visit(name)
check(assemblies['EasyCombat.Runtime'][1]['references']==['Unity.Burst', 'EasyResources.Runtime', 'EasyFeedbacks.Runtime'],'Unexpected combat core dependency')
library=(MODULE/'Editor/CombatAssetLibrary.cs').read_text(encoding='utf-8-sig')
sizes={}
for field in ('Types','Titles','Folders','Codes','Colors'):
    match=re.search(r'\b'+field+r'\s*=\s*\{(.*?)\};',library,re.S)
    check(match is not None,'Missing Studio array '+field)
    if match:
        pattern=r'typeof\s*\(' if field=='Types' else r'new\s+Color\s*\(' if field=='Colors' else r'"(?:\\.|[^"\\])*"'
        sizes[field]=len(re.findall(pattern,match.group(1)))
check(len(set(sizes.values()))==1,'Misaligned Studio categories '+str(sizes))
guids={}
source_count=0
for root in roots:
    for path in root.rglob('*'):
        if path.suffix=='.meta':
            check(path.with_suffix('').exists(),'Orphan metadata '+str(path))
            match=re.search(r'^guid: ([a-f0-9]{32})$',path.read_text(encoding='utf-8-sig'),re.M)
            check(match is not None,'Invalid metadata GUID '+str(path))
            if match:
                guid=match.group(1); check(guid not in guids,'Duplicate GUID '+str(path)); guids[guid]=path
        elif not any(part.endswith('~') for part in path.relative_to(root).parts):
            check(Path(str(path)+'.meta').exists(),'Missing metadata '+str(path))
        if path.suffix=='.cs':
            source_count+=1; text=path.read_text(encoding='utf-8-sig')
            check(not re.search(r'Packages/com\.oreethehat\.easy-(?!game-framework)[^/"\s]+',text),'Old package path '+str(path))
            check(not re.search(r'\[MenuItem\("EasyCombat/',text),'Old editor menu '+str(path))
check('InitializeOnLoad' not in (MODULE/'Editor/ProjectileSetupWindow.cs').read_text(),'Auto-popup still enabled')
# Resolve Attack Gallery serialized references against the module and installed Unity dependencies.
gallery=MODULE/'Samples~/AttackGallery'
needed={}
for path in gallery.rglob('*'):
    if path.suffix in ('.unity','.asset','.prefab','.mat','.tss','.uss'):
        for guid in re.findall(r'guid: ([a-f0-9]{32})',path.read_text(encoding='utf-8-sig')):
            if guid not in guids and not guid.startswith('0000000000000000'): needed.setdefault(guid,[]).append(str(path.relative_to(MODULE)))
unresolved=set(needed)
project=next((p for p in PACKAGE.parents if (p/'ProjectSettings').is_dir()),None)
if project:
    for dependency in (project/'Library/PackageCache').glob('*'):
        if not unresolved: break
        for meta in dependency.rglob('*.meta'):
            match=re.search(r'^guid: ([a-f0-9]{32})$',meta.read_text(encoding='utf-8-sig',errors='replace'),re.M)
            if match: unresolved.discard(match.group(1))
for guid in sorted(unresolved): errors.append('Unresolved gallery dependency '+guid+': '+str(needed[guid]))
report={'passed':not errors,'assemblies':len(assemblies),'sources':source_count,'guids':len(guids),'studioCategories':sizes,'galleryExternalGuids':len(needed),'errors':errors}
print(json.dumps(report,ensure_ascii=False,indent=2))
sys.exit(bool(errors))
