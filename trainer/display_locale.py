# Copyright (c) 2026 eirros. All rights reserved.
"""Read-only localized aliases for item searches; canonical definitions stay raw."""
from functools import lru_cache
from pathlib import Path
import json
from urllib.parse import urlsplit,parse_qs,urlencode

LOCALES=frozenset(('zh-Hans','zh-Hant','en-US','ja-JP','ko-KR'))
FOLDER=Path(__file__).resolve().parent.parent/'localization/trainer'

@lru_cache(maxsize=12)
def _dictionary(locale,stamp):
    path=FOLDER/('content-'+locale+'.json')
    try:
        data=json.loads(path.read_text('utf-8'))
        return data if isinstance(data,dict) else {}
    except (OSError,ValueError):return {}

def names(locale):
    if locale not in LOCALES or locale=='zh-Hans':return {}
    path=FOLDER/('content-'+locale+'.json')
    try:return _dictionary(locale,path.stat().st_mtime_ns)
    except OSError:return {}

def matches(item,words,translations):
    if not words:return True
    original=item.get('search','')
    localized=translations.get(item.get('name',''),'').casefold()
    haystack=original+' '+localized
    return all(word in haystack for word in words)

def public_page(path,locale,fetch,items):
    """Filter translated public definitions without changing IDs or saved data."""
    route=urlsplit(path);query={k:v[0] for k,v in parse_qs(route.query).items()}
    words=query.get('q','').casefold().split()
    if route.path not in ('/api/avatars/page','/api/inventory/page','/api/quests') or locale=='zh-Hans' or not words:
        return fetch(path)
    translations=names(locale)
    if not translations:return fetch(path)
    requested=max(0,int(query.get('page',0)))
    size=max(1,min(100,int(query.get('pageSize',20)))) if route.path.endswith('/page') else 30
    scan=dict(query,q='',page='0')
    if route.path.endswith('/page'):scan['pageSize']='100'
    first=fetch(route._replace(query=urlencode(scan)).geturl())
    def identity(row):
        if route.path=='/api/inventory/page':
            return (row.get('item_space'),row.get('slot_index'),row.get('item_key',row.get('id')))
        return row['id']
    definitions=list(first.get('items',[]));seen={identity(row) for row in definitions}
    total=int(first.get('total',len(definitions)));batch=len(definitions)
    pages=int(first.get('pages',(total+batch-1)//batch if batch else 1))
    for page in range(1,pages):
        scan['page']=str(page)
        response=fetch(route._replace(query=urlencode(scan)).geturl())
        extra=[row for row in response.get('items',[]) if identity(row) not in seen]
        if not extra:break
        definitions.extend(extra);seen.update(identity(row) for row in extra)
    def found(row):
        item_id=row.get('item_key',row.get('id'))
        canonical=items.get(item_id,{}).get('name',row.get('name','')) if route.path.endswith('/page') else row.get('name','')
        haystack=str(item_id)+' '+row.get('name','')+' '+canonical+' '+translations.get(canonical,'')+' '+translations.get(row.get('name',''),'')
        return all(word in haystack.casefold() for word in words)
    selected=[row for row in definitions if found(row)]
    result=dict(first,items=selected[requested*size:(requested+1)*size],page=requested,total=len(selected),pages=max(1,(len(selected)+size-1)//size))
    return result
