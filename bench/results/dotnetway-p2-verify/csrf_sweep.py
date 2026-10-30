import re, subprocess, sys
src = open('/Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/src/Campfire.App/Controllers.fs').read()
token = open('token.txt').read().strip()
B = 'http://127.0.0.1:4412'
rows = re.findall(r'^\s+(post|patch|put|delete) "([^"]+)" "([^"]+)" \(?([\w.]+)', src, re.M)
ids = {'room_id':'104393281','id':'1','message_id':'1','user_id':'me','bot_id':'1','join_code':'abc','push_subscription_id':'1','inbound_email_id':'1','encoded_token':'x','bot_key':'nope-key'}
def path(p):
    p = re.sub(r'\(\.:format\)', '', p)
    return re.sub(r':(\w+)', lambda m: ids.get(m.group(1), '1'), p)
def req(method, url, extra):
    cmd = ['curl','-s','-o','/dev/null','-w','%{http_code}','-X',method,url,'-b',f'session_token={token}'] + extra
    return subprocess.run(cmd, capture_output=True, text=True).stdout
for verb, pat, ep, action in rows:
    url = B + path(pat)
    m = verb.upper()
    cross = req(m, url, ['-H','Sec-Fetch-Site: cross-site','-H','Origin: https://evil.example','--data','a=1'])
    samesite = req(m, url, ['-H','Sec-Fetch-Site: same-site','--data','a=1'])
    js = req(m, url, ['-H','Origin: https://evil.example','-H','Content-Type: application/json','--data','{"a":1}'])
    bodyless = req(m, url, ['-H','Origin: https://evil.example'])
    ovr = req('POST', url, ['-H','Sec-Fetch-Site: cross-site','--data',f'_method={verb}&a=1']) if m != 'POST' else '-'
    print(f'{m:6} {pat:70} {action:40} cross={cross} samesite={samesite} json={js} bodyless={bodyless} override={ovr}')
