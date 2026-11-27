import { test, expect } from '@playwright/test';
import { AD_FRAME_HEADERS, AD_FRAME_HTML } from '../../edge/adFrame';

test('sandboxed banner executes sync scripts and eval without access to the parent', async ({ page }) => {
  const creative = '<div id="creative" style="width:300px;height:250px">loading</div>' +
    '<script src="https://ads.example/sync.js"></script>';
  const parentHtml = '<!doctype html><body><script>' +
    'window.messages=[]; window.secret="private"; window.parentEvalBlocked=false;' +
    'try{eval("1+1")}catch(e){window.parentEvalBlocked=true}' +
    'addEventListener("message",e=>{if(e.source===document.querySelector("iframe").contentWindow&&e.origin==="null")window.messages.push(e.data)});' +
    'window.creative=' + JSON.stringify(creative).replace(/</g,'\\u003c') + ';</script>' +
    '<iframe sandbox="allow-scripts allow-popups allow-popups-to-escape-sandbox allow-forms" src="/ad-frame" style="height:90px;width:320px" ' +
    'onload="if(!this.dataset.sent){this.dataset.sent=1;this.contentWindow.postMessage({type:\'rn-ad-init\',id:\'test-1\',html:window.creative},\'*\')}"></iframe></body>';
  await page.route('https://rongnho.example/**', async route => {
    if (route.request().url().endsWith('/ad-frame')) return route.fulfill({ status: 200, headers: AD_FRAME_HEADERS, body: AD_FRAME_HTML });
    return route.fulfill({ status: 200, headers: {'Content-Type':'text/html','Content-Security-Policy':"default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'unsafe-inline'; frame-src 'self'"}, body: parentHtml });
  });
  await page.route('https://ads.example/sync.js', route => route.fulfill({contentType:'application/javascript',body:
    'document.getElementById("creative").textContent=eval("40+2");' +
    'try{parent.secret;document.body.dataset.escape="bad"}catch(e){document.body.dataset.escape="blocked"};' +
    'document.write("<span id=written>sync</span>");'
  }));
  await page.goto('https://rongnho.example/');
  const frame = page.frameLocator('iframe');
  await expect(frame.locator('#creative')).toHaveText('42');
  await expect(frame.locator('#written')).toHaveText('sync');
  await expect(frame.locator('body')).toHaveAttribute('data-escape','blocked');
  expect(await page.evaluate('window.parentEvalBlocked')).toBe(true);
  await expect.poll(() => page.evaluate('window.messages.some(m=>m.type==="rn-ad-size"&&m.id==="test-1"&&m.h>=250)')).toBe(true);
});

test('opening the sandbox directly never accepts an ad payload', async ({ page }) => {
  await page.route('https://rongnho.example/ad-frame', route => route.fulfill({status:200,headers:AD_FRAME_HEADERS,body:AD_FRAME_HTML}));
  await page.goto('https://rongnho.example/ad-frame');
  await page.evaluate(() => window.postMessage({type:'rn-ad-init',id:'test',html:'<div id="bad">bad</div>'},'*'));
  await expect(page.locator('#bad')).toHaveCount(0);
});
