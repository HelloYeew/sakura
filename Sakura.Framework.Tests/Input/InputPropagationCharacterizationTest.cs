// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

// Phase 0's InputPropagationCharacterizationTest asserted the *recursive* Container.OnX propagation
// that was the regression gate during the input-manager revamp. Phase 7 deleted that recursive path,
// so these characterizations now live against the queue pipeline in InputManagerDispatchTest
// (non-positional) and InputManagerPositionalDispatchTest (positional). This file is intentionally
// left empty; it can be deleted from the project once tooling permits.
