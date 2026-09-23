import test from 'node:test';
import assert from 'node:assert/strict';
import { withRetry } from './client.js';

test('retries transient failures with Polly-style exponential backoff', async () => {
  const delays = [];
  let attempts = 0;

  const result = await withRetry(
    async () => {
      attempts += 1;
      if (attempts < 3) {
        throw new Error('temporary failure');
      }
      return 'ok';
    },
    {
      maxRetries: 2,
      baseDelayMs: 3000,
      onDelay: (ms) => delays.push(ms),
    },
  );

  assert.equal(result, 'ok');
  assert.equal(attempts, 3);
  assert.deepEqual(delays, [3000, 9000]);
});

test('stops retrying after the retry limit is reached', async () => {
  let attempts = 0;

  await assert.rejects(
    () =>
      withRetry(
        async () => {
          attempts += 1;
          throw new Error('still failing');
        },
        {
          maxRetries: 3,
          baseDelayMs: 10,
        },
      ),
    /still failing/,
  );

  assert.equal(attempts, 4);
});
