"""Use case tutorials demonstrating Railway-Oriented Programming with Result in Python."""

from __future__ import annotations

import asyncio
from dataclasses import dataclass
import pytest

from nyssa_ai_result.error import Error
from nyssa_ai_result.error_category import ErrorCategory
from nyssa_ai_result.guard import Guard
from nyssa_ai_result.result import Result, fail, ok, try_


@dataclass(frozen=True, slots=True)
class UserRegistrationRequest:
    username: str
    email: str
    age: int


@dataclass(frozen=True, slots=True)
class RegisteredUser:
    user_id: int
    username: str
    email: str


def test_tutorial_user_registration_pipeline_railway_flow() -> None:
    # Existing usernames in mock database
    existing_users = {"alice", "bob"}

    def validate_request(req: UserRegistrationRequest) -> Result[UserRegistrationRequest]:
        return (
            Guard.against.null_or_whitespace(req.username, parameter_name="username")
            .then(lambda _: Guard.against.pattern_mismatch(req.email, r"^[^@]+@[^@]+\.[^@]+$", parameter_name="email"))
            .then(lambda _: Guard.against.out_of_range(req.age, 18, 120, parameter_name="age"))
            .transform(lambda _: req)
        )

    def check_uniqueness(req: UserRegistrationRequest) -> Result[UserRegistrationRequest]:
        if req.username.lower() in existing_users:
            return fail(
                Error.from_messages(
                    user_message="Username is already taken.",
                    technical_message=f"Conflict: username '{req.username}' exists in repository.",
                    category=ErrorCategory.CONFLICT,
                )
            )
        return ok(req)

    def persist_user(req: UserRegistrationRequest) -> Result[RegisteredUser]:
        new_user = RegisteredUser(user_id=101, username=req.username, email=req.email.lower().strip())
        return ok(new_user)

    def register_pipeline(req: UserRegistrationRequest) -> Result[RegisteredUser]:
        return (
            validate_request(req)
            .then(check_uniqueness)
            .then(persist_user)
        )

    # 1. Successful flow
    valid_req = UserRegistrationRequest(username="charlie", email="charlie@example.com", age=25)
    result_ok = register_pipeline(valid_req)
    assert result_ok.is_success is True
    assert result_ok.value.user_id == 101
    assert result_ok.value.email == "charlie@example.com"

    # 2. Validation failure flow (underage)
    underage_req = UserRegistrationRequest(username="dave", email="dave@example.com", age=16)
    result_fail_val = register_pipeline(underage_req)
    assert result_fail_val.is_failure is True
    assert result_fail_val.errors[0].category == ErrorCategory.VALIDATION
    assert "must be between 18 and 120" in result_fail_val.errors[0].user_message

    # 3. Conflict failure flow (duplicate username)
    duplicate_req = UserRegistrationRequest(username="alice", email="alice2@example.com", age=30)
    result_fail_conflict = register_pipeline(duplicate_req)
    assert result_fail_conflict.is_failure is True
    assert result_fail_conflict.errors[0].category == ErrorCategory.CONFLICT
    assert result_fail_conflict.errors[0].user_message == "Username is already taken."


def test_tutorial_external_boundary_catches_io_exceptions_via_try() -> None:
    def fake_external_payment_gateway(account_id: str, amount_cents: int) -> str:
        if amount_cents > 100000:
            raise ConnectionError("Gateway TCP reset during transaction")
        return f"tx_ref_{account_id}_{amount_cents}"

    def process_payment(account_id: str, amount_cents: int) -> Result[str]:
        return try_(
            lambda: fake_external_payment_gateway(account_id, amount_cents),
            error_handler=lambda exc: Error.from_messages(
                user_message="Payment gateway is currently unavailable. Please retry shortly.",
                technical_message=f"Gateway communication failure: {exc}",
                category=ErrorCategory.UNAVAILABLE,
            ),
        )

    # Success case
    ok_res = process_payment("acc_123", 5000)
    assert ok_res.is_success is True
    assert ok_res.value == "tx_ref_acc_123_5000"

    # External failure caught at explicit boundary
    fail_res = process_payment("acc_123", 200000)
    assert fail_res.is_failure is True
    assert fail_res.errors[0].category == ErrorCategory.UNAVAILABLE
    assert "currently unavailable" in fail_res.errors[0].user_message
    assert "Gateway TCP reset" in fail_res.errors[0].technical_message


def test_tutorial_fail_fast_on_programmer_defect_bubbles_immediately() -> None:
    def broken_business_logic(x: int) -> Result[int]:
        dict_data: dict[str, int] = {}
        return ok(dict_data["missing_key"] + x)

    with pytest.raises(KeyError) as exc_info:
        ok(10).then(broken_business_logic)

    assert "missing_key" in str(exc_info.value)
