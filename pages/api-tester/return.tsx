import ApiRequestTester from "../../component/ApiRequestTester/ApiRequestTester";
import RoleGuard from "../../auth/RoleGuard";

const Return = () => {
  return (
      <RoleGuard allowedRoles={['gateway']}>
          <ApiRequestTester
              title={"API Return Request Tester "}
              placeholder={"https://example.com/api/v1/Gateway/Status"}
              selectedRequest={"ReturnRequest"}
              initialUrl={'http://localhost:8081/api/v1/Gateway/Return'}
          />
      </RoleGuard>
  )
}
export default Return;