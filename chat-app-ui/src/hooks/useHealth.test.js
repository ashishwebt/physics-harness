import test from 'node:test';
import assert from 'node:assert/strict';
import { calculateHealthState } from './useHealth.js';

test('health remains checking for the first minute after a failure', () => {
  const firstFailure = calculateHealthState({
    success: false,
    failureStartedAt: null,
    now: 1000,
    retryWindowMs: 60000,
  });

  assert.deepEqual(firstFailure, {
    status: 'checking',
    failureStartedAt: 1000,
  });

  const beforeThreshold = calculateHealthState({
    success: false,
    failureStartedAt: 1000,
    now: 1000 + 59999,
    retryWindowMs: 60000,
  });

  assert.deepEqual(beforeThreshold, {
    status: 'checking',
    failureStartedAt: 1000,
  });

  const atThreshold = calculateHealthState({
    success: false,
    failureStartedAt: 1000,
    now: 1000 + 60000,
    retryWindowMs: 60000,
  });

  assert.deepEqual(atThreshold, {
    status: 'down',
    failureStartedAt: 1000,
  });
});

test('successful health checks reset the failure timer', () => {
  const result = calculateHealthState({
    success: true,
    failureStartedAt: 5000,
    now: 70000,
    retryWindowMs: 60000,
  });

  assert.deepEqual(result, {
    status: 'ok',
    failureStartedAt: null,
  });
});
