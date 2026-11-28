import { test, expect } from '@playwright/test';
import { readFileSync } from 'node:fs';
import ts from 'typescript';

const loader = ts.transpileModule(readFileSync(new URL('../../src/features/ads/directCreative.ts', import.meta.url), 'utf8'), {
  compilerOptions:{module:ts.ModuleKind.ESNext,target:ts.ScriptTarget.ES2022}
}).outputText;

test('direct native tag can read its page cookie, sees its container, and does not run twice', async ({page}) => {
  let calls = 0;
  await page.route('https://site.example/**', async route => {
    if (route.request().url().endsWith('/loader.js')) return route.fulfill({contentType:'application/javascript',body:loader});
    return route.fulfill({contentType:'text/html',headers:{'Content-Security-Policy':"default-src 'self'; script-src 'self' 'unsafe-inline' https:; style-src 'unsafe-inline'"},body:'<!doctype html><body><div id="one"></div><div id="two"></div></body>'});
  });
  await page.route('https://provider.example/native.js', route => {
    calls++;
    return route.fulfill({contentType:'application/javascript',body:'document.getElementById("container").textContent="cookie="+document.cookie; try{eval("1+1")}catch(e){document.body.dataset.eval="blocked"}'});
  });
  await page.goto('https://site.example/');
  const result = await page.evaluate(async () => {
    const {mountDirectBanner}=await import('/loader.js');
    document.cookie='test=ok; path=/';
    const html='<script async src="https://provider.example/native.js"></script><div id="container"></div>';
    const signal=new AbortController().signal;
    const first=await mountDirectBanner(document.querySelector('#one'), 'one', html, signal,()=>{});
    const duplicate=await mountDirectBanner(document.querySelector('#two'), 'two', html, signal,()=>{});
    return {first,duplicate};
  });
  expect(result).toEqual({first:true,duplicate:false});
  await expect(page.locator('#container')).toHaveText('cookie=test=ok');
  await expect(page.locator('body')).toHaveAttribute('data-eval','blocked');
  expect(calls).toBe(1);
});

test('independent async queues load distinct tags without weakening CSP', async ({page})=>{
  await page.route('https://site.example/**', route=>route.request().url().endsWith('/loader.js')?route.fulfill({contentType:'application/javascript',body:loader}):route.fulfill({contentType:'text/html',headers:{'Content-Security-Policy':"default-src 'self'; script-src 'self' 'unsafe-inline' https:"},body:'<!doctype html><body><div id="one"></div><div id="two"></div><div id="three"></div></body>'}));
  await page.route('https://provider.example/**',route=>route.fulfill({contentType:'application/javascript',body:'(function(){const opt=window.atAsyncOptions.shift();document.getElementById(opt.container).textContent=opt.key;})();'}));
  await page.goto('https://site.example/');
  const result=await page.evaluate(async()=>{
    const path='/loader.js';
    const {mountDirectBanner}=await import(path);
    const html=(key:string)=>'<script>window.atAsyncOptions=window.atAsyncOptions||[];atAsyncOptions.push({key:"'+key+'",container:"ad-'+key+'"})</script><script async src="https://provider.example/'+key+'.js"></script><div id="ad-'+key+'"></div>';
    const sig=new AbortController().signal;
    const first=await mountDirectBanner(document.querySelector('#one'),'one',html('a'),sig,()=>{});
    const second=await mountDirectBanner(document.querySelector('#two'),'two',html('b'),sig,()=>{});
    let reason='';
    const duplicate=await mountDirectBanner(document.querySelector('#three'),'three',html('a'),sig,()=>{},(r:string)=>{reason=r;});
    return {first,second,duplicate,reason};
  });
  expect(result).toEqual({first:true,second:true,duplicate:false,reason:'duplicate'});
  await expect(page.locator('#ad-a')).toHaveText('a');
  await expect(page.locator('#ad-b')).toHaveText('b');
});
