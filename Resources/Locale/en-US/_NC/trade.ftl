nc-store-category-fallback = Miscellaneous
nc-store-contracts-category-empty = No contracts are currently available in this category.
nc-store-contract-category-all = All
nc-store-contract-category-button = { $name } ({ $count })
nc-store-contract-category-all-tooltip = Show every available contract.
nc-store-contract-category-tooltip = Show contracts in the "{ $category }" category.
nc-store-contract-proof-generation-failed = Proof of completion could not be created. The contract has failed.
nc-store-contract-proof-destroyed = Proof item for this contract has been destroyed; contract failed.
nc-store-contract-hunt-target-lost = The hunt target was lost before all stages were completed. The contract has failed.
nc-store-contract-hunt-next-target-spawn-failed = Could not spawn the next hunt target stage. The contract has failed.
nc-store-contract-turn-in-header = Turn in at the machine:
nc-store-contract-turn-in-note = After completion: { $item }
nc-store-contract-action-can-claim-proof = Ready, proof must be turned in
nc-store-contract-progress-caption = Progress
nc-store-contract-progress-value = { $progress } / { $required }
nc-store-contract-title-fallback = Contract
nc-store-contract-confirm-take-title = Accept Contract
nc-store-contract-confirm-skip-title = Skip Contract
nc-store-contract-confirm-take = Accept contract "{ $contract }"?
nc-store-contract-confirm-skip = Skip contract "{ $contract }" and replace it with a new one?
nc-store-contract-confirm-take-action = Accept
nc-store-contract-confirm-skip-action = Skip
nc-store-contract-confirm-no = No
nc-store-contract-progress-caption-delivered = Delivered
nc-store-contract-route-status-available = Route is not accepted yet.
nc-store-contract-route-status-progress = Cargo delivered: { $progress } / { $max }.
nc-store-contract-route-status-delivered = Cargo delivered. Complete the route.
nc-store-contract-route-status-find-cargo = Find the cargo and deliver it along the route.
nc-store-contract-route-status-proof-bearer = Delivery confirmed. Return the proof to the trader; the bearer receives the reward.
nc-store-contract-route-status-proof-return = Delivery confirmed. Return to the trader with the proof.
nc-store-contract-route-status-store-cargo-ready = Cargo delivered. Claim the reward from the trader.
nc-store-contract-route-status-ready = Route complete. Claim the reward from the trader.
nc-store-contract-route-action-available = Accept the delivery route.
nc-store-contract-route-action-progress = Deliver cargo: { $progress } / { $max }.
nc-store-contract-route-action-proof-after-delivery = Fully deliver the cargo to receive one proof of delivery.
nc-store-contract-route-action-wait-confirmation = Wait for delivery confirmation.
nc-store-contract-route-action-proof-bearer = Bring the proof to the trader. It can be handed off, stolen, or sold.
nc-store-contract-route-action-proof = Bring the proof to the trader.
nc-store-contract-route-action-store-cargo-ready = Reward is available from the trader. No proof is needed.
nc-store-contract-route-action-ready = Claim the reward from the trader.
nc-store-contract-offer-pool-tooltip = Offer group: { $pool }
nc-store-contract-route-proof-bearer-note = Delivery proof is bearer-held: the reward goes to whoever brings it to the trader.
nc-store-contract-duration-hours = { $count ->
    [one] { $count } hour
   *[other] { $count } hours
}
nc-store-contract-duration-minutes = { $count ->
    [one] { $count } minute
   *[other] { $count } minutes
}
nc-store-contract-duration-seconds = { $count ->
    [one] { $count } second
   *[other] { $count } seconds
}
nc-store-contract-ghost-role-manifest-role = Fugitive
nc-store-contract-ghost-role-character-briefing = You are the target of "{ $contract }". Follow the role rules.
nc-store-contract-ghost-role-character-briefing-survival = You are the target of "{ $contract }". Survive for { $time } and keep hunters from turning you in.
nc-store-contract-ghost-role-roundend-header = [bold][color=#c9a66b]Contract Targets[/color][/bold]
nc-store-contract-ghost-role-roundend-line-contract = [color=#c9a66b]•[/color] [bold]{ $contract }[/bold]
nc-store-contract-ghost-role-roundend-line-role = [color=#9ab7d6]Target:[/color] { $role }
nc-store-contract-ghost-role-roundend-line-player = [color=#9ab7d6]Player:[/color] { $player }
nc-store-contract-ghost-role-roundend-line-result = [color=#9ab7d6]Result:[/color] { $result }
nc-store-contract-ghost-role-roundend-unknown-role = unknown target
nc-store-contract-ghost-role-roundend-no-player = unclaimed
nc-store-contract-ghost-role-roundend-result-waiting = unclaimed
nc-store-contract-ghost-role-roundend-result-active = not delivered
nc-store-contract-ghost-role-roundend-result-delivered-alive = delivered alive
nc-store-contract-ghost-role-roundend-result-delivered-dead = body delivered
nc-store-contract-ghost-role-roundend-result-survived = survived { $time }
nc-store-contract-ghost-role-roundend-result-not-accepted = offer expired
nc-store-contract-ghost-role-roundend-result-target-lost = target lost
nc-store-contract-ghost-role-roundend-result-target-rotten = body rotted
