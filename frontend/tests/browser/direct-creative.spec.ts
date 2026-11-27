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
